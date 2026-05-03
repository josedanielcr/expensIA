using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace functions;

public class ReviewTransactions
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";
    private const string ErrorTokenRequired = "Authorization header with Bearer token is required.";
    private const string ErrorRequestRequired = "Request body is required.";
    private readonly ILogger<ReviewTransactions> _logger;
    private readonly GoogleTokenValidator _tokenValidator;
    private readonly TransactionReviewService _reviewService;

    public ReviewTransactions(
        ILogger<ReviewTransactions> logger,
        GoogleTokenValidator tokenValidator,
        TransactionReviewService reviewService)
    {
        _logger = logger;
        _tokenValidator = tokenValidator;
        _reviewService = reviewService;
    }

    [Function("GetPendingReviewTransactions")]
    public async Task<IActionResult> GetPendingReviewTransactions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/transactions")] HttpRequest req,
        FunctionContext context,
        CancellationToken ct)
    {
        var owner = await ValidateOwnerAsync(req, ct);
        var entries = await _reviewService.GetPendingReviewTransactionsAsync(owner, ct);

        _logger.LogInformation(
            "Pending review transactions fetched. InvocationId={InvocationId} Count={Count}",
            context.InvocationId,
            entries.Count);

        return new OkObjectResult(new
        {
            total = entries.Count,
            entries,
        });
    }

    [Function("ReviewTransactionAction")]
    public async Task<IActionResult> ReviewTransactionAction(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "review/transactions/{transactionId}")] HttpRequest req,
        string transactionId,
        FunctionContext context,
        CancellationToken ct)
    {
        var payload = await req.ReadFromJsonAsync<ReviewTransactionActionRequest>(cancellationToken: ct);
        if (payload is null)
            return new BadRequestObjectResult(new { error = ErrorRequestRequired });
        if (!Guid.TryParse(transactionId, out var transactionGuid))
            return new BadRequestObjectResult(new { error = "Transaction id is invalid." });

        var owner = await ValidateOwnerAsync(req, ct);
        ReviewTransactionDto entry;
        try
        {
            entry = await _reviewService.ApplyActionAsync(transactionGuid, payload, owner, ct);
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        _logger.LogInformation(
            "Review transaction action applied. InvocationId={InvocationId} TransactionId={TransactionId} Action={Action}",
            context.InvocationId,
            transactionId,
            payload.Action);

        return new OkObjectResult(new
        {
            ok = true,
            entry,
        });
    }

    private async Task<GoogleTokenInfo> ValidateOwnerAsync(HttpRequest req, CancellationToken ct)
    {
        var token = ExtractTokenFromAuthorizationHeader(req);
        if (string.IsNullOrWhiteSpace(token))
            throw new UnauthorizedAccessException(ErrorTokenRequired);

        return await _tokenValidator.ValidateTokenAsync(token, ct)
            ?? throw new UnauthorizedAccessException("Invalid Google token (empty response).");
    }

    private static string ExtractTokenFromAuthorizationHeader(HttpRequest req)
    {
        if (!req.Headers.TryGetValue(AuthorizationHeader, out var headerValues))
            return string.Empty;

        var header = headerValues.ToString().Trim();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return header[BearerPrefix.Length..].Trim();

        return header;
    }
}
