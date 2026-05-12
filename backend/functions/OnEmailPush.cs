using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace functions;

public class OnEmailPush
{
    private const string FunctionName = "OnEmailPush";
    private const string HttpMethodPost = "post";
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    private const string ErrorRequestRequired = "Request body is required.";
    private const string ErrorTokenRequired = "Authorization header with Bearer token is required.";
    private const string ErrorEmailsRequired = "emails must contain at least one entry.";
    private const string CurrencyUsd = "USD";
    private const string CurrencyCrc = "CRC";
    private const string NonTransactionCategory = "N/A";
    private const string UnknownCategory = "Uncategorized";
    private const string CurrencyCodePattern = @"\(([A-Za-z]{3})\)";
    private static readonly Regex CurrencyCodeRegex = new(CurrencyCodePattern, RegexOptions.Compiled);
    private readonly ILogger<OnEmailPush> _logger;
    private readonly GoogleTokenValidator _tokenValidator;
    private readonly IOpenAiExpenseParser _expenseParser;
    private readonly ExchangeRateService _exchangeRateService;

    public OnEmailPush(
        ILogger<OnEmailPush> logger,
        GoogleTokenValidator tokenValidator,
        IOpenAiExpenseParser expenseParser,
        ExchangeRateService exchangeRateService)
    {
        _logger = logger;
        _tokenValidator = tokenValidator;
        _expenseParser = expenseParser;
        _exchangeRateService = exchangeRateService;
    }

    [Function(FunctionName)]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, HttpMethodPost)] HttpRequest req,
        FunctionContext context,
        CancellationToken ct)
    {
        var payload = await req.ReadFromJsonAsync<OnEmailPushRequest>(cancellationToken: ct);
        if (payload is null)
        {
            LogBadRequest(context, ErrorRequestRequired);
            return BuildBadRequest(ErrorRequestRequired);
        }

        var token = ExtractTokenFromAuthorizationHeader(req);
        if (string.IsNullOrWhiteSpace(token))
        {
            LogBadRequest(context, ErrorTokenRequired);
            return BuildBadRequest(ErrorTokenRequired);
        }

        var payloadValidationError = ValidateRequest(payload);
        if (payloadValidationError is not null)
        {
            LogBadRequest(context, payloadValidationError);
            return BuildBadRequest(payloadValidationError);
        }

        var tokenInfo = await _tokenValidator.ValidateTokenAsync(token, ct);
        var userEmail = NormalizeTelemetryValue(tokenInfo?.Email);
        var googleUserId = ResolveGoogleUserId(tokenInfo);
        StoreTelemetryContext(context, userEmail, googleUserId);

        _logger.LogInformation(
            "OnEmailPush authenticated. FunctionName={FunctionName} InvocationId={InvocationId} UserEmail={UserEmail} GoogleUserId={GoogleUserId}",
            FunctionName,
            context.InvocationId,
            userEmail,
            googleUserId);

        var parsedEntries = await ParseEmailsAsync(
            payload.Emails,
            payload.Categories,
            payload.ExclusionRules,
            ct);
        var usdConversionCount = await ConvertUsdEntriesToCrcAsync(parsedEntries, ct);
        var orderedEntries = OrderEntriesByDateOldestFirst(parsedEntries);
        LogRunSummary(context.InvocationId, userEmail, googleUserId, payload, orderedEntries, usdConversionCount);

        return BuildSuccessResponse(orderedEntries);
    }

    private static string? ValidateRequest(OnEmailPushRequest payload)
    {
        if (payload.Emails.Count == 0)
            return ErrorEmailsRequired;

        return null;
    }

    private void LogBadRequest(FunctionContext context, string reason)
    {
        _logger.LogWarning(
            "OnEmailPush rejected request. FunctionName={FunctionName} InvocationId={InvocationId} Status={Status} Reason={Reason}",
            FunctionName,
            context.InvocationId,
            "BadRequest",
            reason);
    }

    private void LogRunSummary(
        string invocationId,
        string userEmail,
        string googleUserId,
        OnEmailPushRequest payload,
        IReadOnlyCollection<ExpenseParseResult> entries,
        int usdConversionCount)
    {
        _logger.LogInformation(
            "OnEmailPush completed. FunctionName={FunctionName} InvocationId={InvocationId} Status={Status} UserEmail={UserEmail} GoogleUserId={GoogleUserId} EmailCount={EmailCount} CategoryCount={CategoryCount} ExclusionRuleCount={ExclusionRuleCount} ParsedEntries={ParsedCount} NonTransactionEntries={NonTransactionCount} UsdConversionCount={UsdConversionCount} CategoryDistribution={CategoryDistribution}",
            FunctionName,
            invocationId,
            "Success",
            userEmail,
            googleUserId,
            payload.Emails.Count,
            payload.Categories.Count,
            payload.ExclusionRules.Count,
            entries.Count,
            CountNonTransactionEntries(entries),
            usdConversionCount,
            BuildCategoryDistribution(entries));
    }

    private static void StoreTelemetryContext(FunctionContext context, string userEmail, string googleUserId)
    {
        if (!string.IsNullOrWhiteSpace(userEmail))
            context.Items[global::TelemetryContextKeys.UserEmail] = userEmail;

        if (!string.IsNullOrWhiteSpace(googleUserId))
            context.Items[global::TelemetryContextKeys.GoogleUserId] = googleUserId;
    }

    private static string ResolveGoogleUserId(GoogleTokenInfo? tokenInfo)
    {
        if (tokenInfo is null)
            return string.Empty;

        var userId = NormalizeTelemetryValue(tokenInfo.UserId);
        return userId.Length > 0 ? userId : NormalizeTelemetryValue(tokenInfo.Sub);
    }

    private static int CountNonTransactionEntries(IReadOnlyCollection<ExpenseParseResult> entries) =>
        entries.Count(entry =>
            string.Equals(
                NormalizeTelemetryValue(entry.Category),
                NonTransactionCategory,
                StringComparison.OrdinalIgnoreCase));

    private static string BuildCategoryDistribution(IReadOnlyCollection<ExpenseParseResult> entries)
    {
        var categoryCounts = entries
            .GroupBy(entry => NormalizeCategoryForTelemetry(entry.Category), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return JsonSerializer.Serialize(categoryCounts);
    }

    private static string NormalizeCategoryForTelemetry(string? category)
    {
        var normalized = NormalizeTelemetryValue(category);
        return normalized.Length == 0 ? UnknownCategory : normalized;
    }

    private static string NormalizeTelemetryValue(string? value) =>
        value?.Trim() ?? string.Empty;

    private async Task<List<ExpenseParseResult>> ParseEmailsAsync(
        IReadOnlyCollection<EmailEntry> emails,
        IReadOnlyCollection<string> categories,
        IReadOnlyCollection<CategoryExclusionRule> exclusionRules,
        CancellationToken ct)
    {
        var emailList = emails as IReadOnlyList<EmailEntry> ?? emails.ToList();
        return await _expenseParser.ParseBatchAsync(emailList, categories, exclusionRules, ct);
    }

    private static IActionResult BuildBadRequest(string message) =>
        new BadRequestObjectResult(new { error = message });

    private static string ExtractTokenFromAuthorizationHeader(HttpRequest req)
    {
        if (!req.Headers.TryGetValue(AuthorizationHeader, out var headerValues))
            return string.Empty;

        var header = headerValues.ToString().Trim();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return header[BearerPrefix.Length..].Trim();

        return header;
    }

    private static IActionResult BuildSuccessResponse(List<ExpenseParseResult> parsedEntries) =>
        new OkObjectResult(new
        {
            total = parsedEntries.Count,
            entries = parsedEntries,
        });

    private static List<ExpenseParseResult> OrderEntriesByDateOldestFirst(
        IReadOnlyList<ExpenseParseResult> entries)
    {
        var indexed = entries
            .Select((entry, index) => new
            {
                Entry = entry,
                Index = index,
                SortDate = TryParseEntryDate(entry.Date),
            })
            .ToList();

        indexed.Sort((left, right) =>
        {
            var leftHasDate = left.SortDate.HasValue;
            var rightHasDate = right.SortDate.HasValue;

            if (leftHasDate && rightHasDate)
            {
                var dateCompare = DateTimeOffset.Compare(left.SortDate!.Value, right.SortDate!.Value);
                if (dateCompare != 0)
                    return dateCompare;
            }
            else if (leftHasDate != rightHasDate)
            {
                return leftHasDate ? -1 : 1;
            }

            return left.Index.CompareTo(right.Index);
        });

        return indexed.Select(item => item.Entry).ToList();
    }

    private static DateTimeOffset? TryParseEntryDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
            return null;

        var supportedFormats = new[]
        {
            "dd/MM/yyyy",
            "d/M/yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
        };

        var value = NormalizeDateForSorting(rawDate);

        // Slash-based dates are expected in local day-first format.
        if (value.Contains('/'))
        {
            if (DateTimeOffset.TryParseExact(
                    value,
                    new[] { "dd/MM/yyyy", "d/M/yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var dayFirstParsed))
            {
                return dayFirstParsed;
            }
        }

        if (DateTimeOffset.TryParseExact(
                value,
                supportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsedExact))
        {
            return parsedExact;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedGeneric))
        {
            return parsedGeneric;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out parsedGeneric))
        {
            return parsedGeneric;
        }

        return null;
    }

    private static string NormalizeDateForSorting(string rawDate)
    {
        var value = rawDate.Trim();
        if (value.Length == 0)
            return string.Empty;

        // Drop trailing timezone labels like "(UTC)" while keeping numeric offset.
        value = Regex.Replace(value, @"\s*\([A-Za-z]{2,8}\)\s*$", string.Empty);
        // Convert RFC 2822 offset style (+0000) to ISO style (+00:00) for stable parsing.
        value = Regex.Replace(value, @"([+-]\d{2})(\d{2})\b", "$1:$2");
        // Keep tokenization predictable.
        value = Regex.Replace(value, @"\s+", " ").Trim();

        return value;
    }

    private async Task<int> ConvertUsdEntriesToCrcAsync(
        IReadOnlyCollection<ExpenseParseResult> entries,
        CancellationToken ct)
    {
        var usdEntries = entries
            .Where(IsUsdCurrencyEntry)
            .ToList();
        if (usdEntries.Count == 0)
            return 0;

        var conversionRate = await _exchangeRateService.GetUsdToCrcRateAsync(ct);
        var convertedCount = 0;
        foreach (var entry in usdEntries)
        {
            if (!decimal.TryParse(entry.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var originalAmount))
                continue;

            var originalUsdAmount = FormatAmount(originalAmount);
            var convertedAmount = decimal.Round(originalAmount * conversionRate, 0, MidpointRounding.AwayFromZero);
            entry.Amount = FormatAmount(convertedAmount);
            entry.Description = ReplaceCurrencyCode(entry.Description, CurrencyUsd, CurrencyCrc);
            entry.Description = AppendOriginalUsdAmount(entry.Description, originalUsdAmount);
            convertedCount++;
        }

        return convertedCount;
    }

    private static bool IsUsdCurrencyEntry(ExpenseParseResult entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Description))
            return false;

        var match = CurrencyCodeRegex.Match(entry.Description);
        if (!match.Success)
            return false;

        var detectedCode = match.Groups[1].Value;
        return string.Equals(detectedCode, CurrencyUsd, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReplaceCurrencyCode(string description, string fromCode, string toCode)
    {
        return CurrencyCodeRegex.Replace(
            description ?? string.Empty,
            match =>
            {
                var currentCode = match.Groups[1].Value;
                if (!string.Equals(currentCode, fromCode, StringComparison.OrdinalIgnoreCase))
                    return match.Value;

                return $"({toCode})";
            },
            1);
    }

    private static string FormatAmount(decimal amount)
    {
        var normalized = amount.ToString("0.############################", CultureInfo.InvariantCulture);
        return normalized.TrimEnd('0').TrimEnd('.');
    }

    private static string AppendOriginalUsdAmount(string description, string usdAmount)
    {
        if (string.IsNullOrWhiteSpace(usdAmount))
            return description ?? string.Empty;

        var currentDescription = description?.Trim() ?? string.Empty;
        var suffix = $"({usdAmount}$)";

        if (currentDescription.Length == 0)
            return suffix;

        return $"{currentDescription} {suffix}";
    }
}
