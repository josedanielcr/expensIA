using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

public sealed class TransactionReviewService
{
    private const string CurrencyCodePattern = @"\(([A-Za-z]{3})\)";
    private const string ActionApprove = "approve";
    private const string ActionMarkSheetSynced = "mark_sheet_synced";
    private const string NoteApproved = "Aprobada desde revisión.";
    private const string NoteCorrected = "Corregida desde revisión.";
    private const string NoteSheetSynced = "Sincronizada con Google Sheets desde revisión.";
    private static readonly Regex CurrencyCodeRegex = new(CurrencyCodePattern, RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AiGastosDbContext _db;

    public TransactionReviewService(AiGastosDbContext db)
    {
        _db = db;
    }

    public async Task<List<ReviewTransactionDto>> GetPendingReviewTransactionsAsync(
        GoogleTokenInfo owner,
        CancellationToken ct)
    {
        var ownerSub = GetOwnerSub(owner);
        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.OwnerGoogleSub == ownerSub &&
                transaction.ReviewStatus == TransactionReviewStatus.PendingReview &&
                transaction.SheetSyncStatus == SheetSyncStatus.NotReady)
            .OrderBy(transaction => transaction.CreatedAt)
            .ToListAsync(ct);
        return transactions.Select(ToDto).ToList();
    }

    public async Task<ReviewTransactionDto> ApplyActionAsync(
        Guid transactionId,
        ReviewTransactionActionRequest request,
        GoogleTokenInfo actor,
        CancellationToken ct)
    {
        var action = NormalizeOptional(request.Action) ?? ActionApprove;
        return action switch
        {
            ActionApprove => await ApproveAsync(transactionId, request, actor, ct),
            ActionMarkSheetSynced => await MarkSheetSyncedAsync(transactionId, request, actor, ct),
            _ => throw new InvalidOperationException("Acción de revisión no soportada."),
        };
    }

    private async Task<ReviewTransactionDto> ApproveAsync(
        Guid transactionId,
        ReviewTransactionActionRequest request,
        GoogleTokenInfo actor,
        CancellationToken ct)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, actor, ct);
        if (transaction.ReviewStatus != TransactionReviewStatus.PendingReview)
            throw new InvalidOperationException("La transacción no está pendiente de revisión.");

        var previousValues = BuildSnapshot(transaction);
        var newDate = TryParseDateOnly(request.Date);
        var newAmount = TryParseAmount(request.Amount)
            ?? throw new InvalidOperationException("El monto debe ser numérico.");
        var newCategory = NormalizeOptional(request.Category)
            ?? throw new InvalidOperationException("La categoría es requerida.");
        var newDescription = NormalizeOptional(request.Description)
            ?? throw new InvalidOperationException("La descripción es requerida.");

        var changed = transaction.TransactionDate != newDate ||
                      transaction.Amount != newAmount ||
                      !string.Equals(transaction.Category, newCategory, StringComparison.Ordinal) ||
                      !string.Equals(transaction.Description, newDescription, StringComparison.Ordinal);

        transaction.TransactionDate = newDate;
        transaction.Amount = newAmount;
        transaction.Category = newCategory;
        transaction.Description = newDescription;
        transaction.Merchant = ExtractMerchant(newDescription);
        transaction.Currency = ExtractCurrency(newDescription);
        transaction.ReviewStatus = changed
            ? TransactionReviewStatus.Corrected
            : TransactionReviewStatus.Approved;
        transaction.ReviewReason = null;
        transaction.SheetSyncStatus = SheetSyncStatus.Ready;
        transaction.ParsedPayloadJson = SerializeJson(new
        {
            date = FormatDate(transaction.TransactionDate),
            amount = FormatAmount(transaction.Amount),
            category = transaction.Category,
            description = transaction.Description,
            confidence_score = transaction.ConfidenceScore,
        });
        transaction.UpdatedAt = DateTimeOffset.UtcNow;

        AddReviewEvent(
            transaction,
            actor,
            changed ? ReviewEventType.Corrected : ReviewEventType.Approved,
            previousValues,
            BuildSnapshot(transaction),
            changed ? NoteCorrected : NoteApproved);
        await RecalculateSyncRunCountsAsync(transaction.SyncRunId, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(transaction);
    }

    private async Task<ReviewTransactionDto> MarkSheetSyncedAsync(
        Guid transactionId,
        ReviewTransactionActionRequest request,
        GoogleTokenInfo actor,
        CancellationToken ct)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, actor, ct);
        if (transaction.SheetSyncStatus != SheetSyncStatus.Ready)
            throw new InvalidOperationException("La transacción no está lista para sincronizar con Google Sheets.");

        var previousValues = BuildSnapshot(transaction);
        transaction.SheetSyncStatus = SheetSyncStatus.SheetSynced;
        transaction.SheetSyncedAt = DateTimeOffset.UtcNow;
        transaction.SheetRowId = NormalizeOptional(request.SheetRowId);
        transaction.SheetError = null;
        transaction.UpdatedAt = transaction.SheetSyncedAt.Value;

        AddReviewEvent(
            transaction,
            actor,
            ReviewEventType.MarkedSheetSynced,
            previousValues,
            BuildSnapshot(transaction),
            NoteSheetSynced);
        await RecalculateSyncRunCountsAsync(transaction.SyncRunId, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(transaction);
    }

    private async Task<StoredTransaction> GetOwnedTransactionAsync(
        Guid transactionId,
        GoogleTokenInfo owner,
        CancellationToken ct)
    {
        var ownerSub = GetOwnerSub(owner);
        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(
                row => row.Id == transactionId && row.OwnerGoogleSub == ownerSub,
                ct);
        return transaction ?? throw new InvalidOperationException("No se encontró la transacción.");
    }

    private async Task RecalculateSyncRunCountsAsync(Guid? syncRunId, CancellationToken ct)
    {
        if (!syncRunId.HasValue)
            return;

        var syncRun = await _db.SyncRuns.FirstOrDefaultAsync(row => row.Id == syncRunId.Value, ct);
        if (syncRun is null)
            return;

        var transactions = await _db.Transactions
            .Where(row => row.SyncRunId == syncRunId.Value)
            .ToListAsync(ct);

        syncRun.PendingReviewCount = transactions.Count(row => row.ReviewStatus == TransactionReviewStatus.PendingReview);
        syncRun.ApprovedCount = transactions.Count(row =>
            row.ReviewStatus == TransactionReviewStatus.Approved ||
            row.ReviewStatus == TransactionReviewStatus.Corrected);
        syncRun.SheetReadyCount = transactions.Count(row => row.SheetSyncStatus == SheetSyncStatus.Ready);
        syncRun.SheetSyncedCount = transactions.Count(row => row.SheetSyncStatus == SheetSyncStatus.SheetSynced);
        syncRun.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void AddReviewEvent(
        StoredTransaction transaction,
        GoogleTokenInfo actor,
        ReviewEventType eventType,
        object previousValues,
        object newValues,
        string note)
    {
        _db.ReviewEvents.Add(new ReviewEvent
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            OwnerGoogleSub = transaction.OwnerGoogleSub,
            OwnerEmail = transaction.OwnerEmail,
            ActorGoogleSub = GetOwnerSub(actor),
            ActorEmail = GetOwnerEmail(actor),
            EventType = eventType,
            PreviousValuesJson = SerializeJson(previousValues),
            NewValuesJson = SerializeJson(newValues),
            Note = note,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static ReviewTransactionDto ToDto(StoredTransaction transaction)
    {
        return new ReviewTransactionDto
        {
            Id = transaction.Id.ToString("D"),
            Date = FormatDate(transaction.TransactionDate),
            Amount = FormatAmount(transaction.Amount),
            Category = transaction.Category ?? string.Empty,
            Description = transaction.Description ?? string.Empty,
            ConfidenceScore = transaction.ConfidenceScore,
            ReviewReason = transaction.ReviewReason ?? string.Empty,
            ReviewStatus = ToSnakeCase(transaction.ReviewStatus),
            SheetSyncStatus = ToSnakeCase(transaction.SheetSyncStatus),
            Subject = transaction.Subject ?? string.Empty,
            Sender = transaction.EmailFrom ?? string.Empty,
        };
    }

    private static object BuildSnapshot(StoredTransaction transaction)
        => new
        {
            date = FormatDate(transaction.TransactionDate),
            amount = FormatAmount(transaction.Amount),
            category = transaction.Category,
            description = transaction.Description,
            reviewStatus = ToSnakeCase(transaction.ReviewStatus),
            sheetSyncStatus = ToSnakeCase(transaction.SheetSyncStatus),
            sheetRowId = transaction.SheetRowId,
        };

    private static string GetOwnerSub(GoogleTokenInfo owner)
    {
        var ownerSub = owner.UserId ?? owner.Sub;
        if (string.IsNullOrWhiteSpace(ownerSub))
            throw new UnauthorizedAccessException("Google token is missing a user id.");

        return ownerSub;
    }

    private static string GetOwnerEmail(GoogleTokenInfo owner)
    {
        if (string.IsNullOrWhiteSpace(owner.Email))
            throw new UnauthorizedAccessException("Google token is missing an email.");

        return owner.Email;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static decimal? TryParseAmount(string? rawAmount)
    {
        if (string.IsNullOrWhiteSpace(rawAmount))
            return null;

        return decimal.TryParse(rawAmount.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private static DateOnly? TryParseDateOnly(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
            return null;

        if (DateOnly.TryParseExact(rawDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly;

        return null;
    }

    private static string? ExtractCurrency(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var match = CurrencyCodeRegex.Match(description);
        if (!match.Success)
            return null;

        return match.Groups[1].Value.ToUpperInvariant();
    }

    private static string? ExtractMerchant(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var normalized = description.Trim();
        var currencyIndex = normalized.IndexOf("(", StringComparison.Ordinal);
        var merchant = currencyIndex > 0
            ? normalized[..currencyIndex].Trim()
            : normalized;

        return string.IsNullOrWhiteSpace(merchant) ? null : merchant;
    }

    private static string FormatDate(DateOnly? date)
        => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatAmount(decimal? amount)
        => amount?.ToString("0.############################", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string ToSnakeCase(TransactionReviewStatus status)
        => status switch
        {
            TransactionReviewStatus.PendingReview => "pending_review",
            TransactionReviewStatus.Approved => "approved",
            TransactionReviewStatus.Corrected => "corrected",
            _ => status.ToString(),
        };

    private static string ToSnakeCase(SheetSyncStatus status)
        => status switch
        {
            SheetSyncStatus.NotReady => "not_ready",
            SheetSyncStatus.Ready => "ready",
            SheetSyncStatus.SheetSynced => "sheet_synced",
            SheetSyncStatus.Failed => "failed",
            _ => status.ToString(),
        };

    private static string SerializeJson<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);
}
