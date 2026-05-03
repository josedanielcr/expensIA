using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public sealed class TransactionPersistenceService
{
    private const string ParserVersion = "milestone-1";
    private const string ConfigOpenAiModel = "OPENAI_MODEL";
    private const string CurrencyCodePattern = @"\(([A-Za-z]{3})\)";
    private const string NonTransactionCategory = "N/A";
    private const decimal AutoApprovalConfidenceThreshold = 0.80m;
    private const string LowConfidenceReviewReason = "Confianza menor al 80%; requiere revisión antes de enviarse a Google Sheets.";
    private const string MissingConfidenceReviewReason = "Score de confianza ausente; requiere revisión antes de enviarse a Google Sheets.";
    private static readonly Regex CurrencyCodeRegex = new(CurrencyCodePattern, RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AiGastosDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TransactionPersistenceService> _logger;

    public TransactionPersistenceService(
        AiGastosDbContext db,
        IConfiguration configuration,
        ILogger<TransactionPersistenceService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SyncRun> StartSyncRunAsync(
        GoogleTokenInfo owner,
        int emailCount,
        string invocationId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var syncRun = new SyncRun
        {
            Id = Guid.NewGuid(),
            OwnerGoogleSub = GetOwnerSub(owner),
            OwnerEmail = GetOwnerEmail(owner),
            Status = SyncRunStatus.Running,
            EmailsReceivedCount = emailCount,
            MetadataJson = SerializeJson(new { invocationId }),
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.SyncRuns.Add(syncRun);
        await _db.SaveChangesAsync(ct);
        return syncRun;
    }

    public async Task<TransactionPersistenceResult> PersistParsedTransactionsAsync(
        SyncRun syncRun,
        IReadOnlyList<EmailEntry> emails,
        IReadOnlyList<ExpenseParseResult> parsedEntries,
        GoogleTokenInfo owner,
        CancellationToken ct)
    {
        var ownerSub = GetOwnerSub(owner);
        var ownerEmail = GetOwnerEmail(owner);
        var rows = BuildTransactionRows(syncRun.Id, emails, parsedEntries, ownerSub, ownerEmail);
        var messageIds = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.MessageId))
            .Select(row => row.MessageId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var contentHashes = rows
            .Where(row => string.IsNullOrWhiteSpace(row.MessageId) && !string.IsNullOrWhiteSpace(row.ContentHash))
            .Select(row => row.ContentHash!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingMessageIds = messageIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _db.Transactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.OwnerGoogleSub == ownerSub &&
                    transaction.MessageId != null &&
                    messageIds.Contains(transaction.MessageId))
                .Select(transaction => transaction.MessageId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var existingContentHashes = contentHashes.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _db.Transactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.OwnerGoogleSub == ownerSub &&
                    transaction.MessageId == null &&
                    transaction.ContentHash != null &&
                    contentHashes.Contains(transaction.ContentHash))
                .Select(transaction => transaction.ContentHash!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var insertedRows = new List<StoredTransaction>();
        var duplicates = 0;
        foreach (var row in rows)
        {
            if (IsDuplicate(row, existingMessageIds, existingContentHashes))
            {
                duplicates++;
                continue;
            }

            _db.Transactions.Add(row);
            insertedRows.Add(row);

            if (!string.IsNullOrWhiteSpace(row.MessageId))
                existingMessageIds.Add(row.MessageId);
            else if (!string.IsNullOrWhiteSpace(row.ContentHash))
                existingContentHashes.Add(row.ContentHash);
        }

        syncRun.EmailsProcessedCount = parsedEntries.Count;
        syncRun.TransactionsCreatedCount = insertedRows.Count;
        syncRun.DuplicatesCount = duplicates;
        syncRun.ApprovedCount = insertedRows.Count(row => row.ReviewStatus == TransactionReviewStatus.Approved);
        syncRun.SheetReadyCount = insertedRows.Count(row => row.SheetSyncStatus == SheetSyncStatus.Ready);
        syncRun.PendingReviewCount = insertedRows.Count(row => row.ReviewStatus == TransactionReviewStatus.PendingReview);
        syncRun.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Transactions persisted. SyncRunId={SyncRunId} Created={CreatedCount} Duplicates={DuplicateCount}",
            syncRun.Id,
            insertedRows.Count,
            duplicates);

        return new TransactionPersistenceResult(insertedRows.Count, duplicates);
    }

    public static bool IsSheetReadyAfterParsing(ExpenseParseResult parsed)
        => !RequiresReview(parsed);

    public async Task CompleteSyncRunAsync(SyncRun syncRun, CancellationToken ct)
    {
        syncRun.Status = syncRun.ErrorCount > 0
            ? SyncRunStatus.CompletedWithErrors
            : SyncRunStatus.Completed;
        syncRun.CompletedAt = DateTimeOffset.UtcNow;
        syncRun.UpdatedAt = syncRun.CompletedAt.Value;
        await _db.SaveChangesAsync(ct);
    }

    public async Task FailSyncRunAsync(SyncRun syncRun, Exception exception, CancellationToken ct)
    {
        syncRun.Status = SyncRunStatus.Failed;
        syncRun.ErrorCount = Math.Max(syncRun.ErrorCount, 1);
        syncRun.ErrorsJson = SerializeJson(new[]
        {
            new
            {
                type = exception.GetType().Name,
                message = exception.Message,
            },
        });
        syncRun.CompletedAt = DateTimeOffset.UtcNow;
        syncRun.UpdatedAt = syncRun.CompletedAt.Value;
        await _db.SaveChangesAsync(ct);
    }

    private List<StoredTransaction> BuildTransactionRows(
        Guid syncRunId,
        IReadOnlyList<EmailEntry> emails,
        IReadOnlyList<ExpenseParseResult> parsedEntries,
        string ownerSub,
        string ownerEmail)
    {
        var count = Math.Min(emails.Count, parsedEntries.Count);
        var rows = new List<StoredTransaction>(count);
        for (var i = 0; i < count; i++)
        {
            var email = emails[i];
            var parsed = parsedEntries[i];
            var now = DateTimeOffset.UtcNow;
            var messageId = NormalizeOptional(email.MessageId);
            var contentHash = string.IsNullOrWhiteSpace(messageId)
                ? BuildContentHash(email)
                : null;
            var requiresReview = RequiresReview(parsed);

            rows.Add(new StoredTransaction
            {
                Id = Guid.NewGuid(),
                OwnerGoogleSub = ownerSub,
                OwnerEmail = ownerEmail,
                SyncRunId = syncRunId,
                MessageId = messageId,
                ContentHash = contentHash,
                Subject = NormalizeOptional(email.Subject),
                EmailFrom = NormalizeOptional(email.Sender),
                EmailReceivedAt = TryParseDateTimeOffset(email.Date),
                Merchant = ExtractMerchant(parsed.Description),
                Description = NormalizeOptional(parsed.Description),
                Category = NormalizeOptional(parsed.Category),
                TransactionDate = TryParseDateOnly(parsed.Date),
                Amount = TryParseAmount(parsed.Amount),
                Currency = ExtractCurrency(parsed.Description),
                ConfidenceScore = parsed.ConfidenceScore,
                ReviewStatus = requiresReview
                    ? TransactionReviewStatus.PendingReview
                    : TransactionReviewStatus.Approved,
                ReviewReason = requiresReview ? BuildReviewReason(parsed) : null,
                SheetSyncStatus = requiresReview
                    ? SheetSyncStatus.NotReady
                    : SheetSyncStatus.Ready,
                ParserVersion = ParserVersion,
                ParserModel = _configuration[ConfigOpenAiModel],
                ParsedAt = now,
                ParsedPayloadJson = SerializeJson(parsed),
                EmailMetadataJson = SerializeJson(new
                {
                    messageId,
                    subject = NormalizeOptional(email.Subject),
                    sender = NormalizeOptional(email.Sender),
                    date = NormalizeOptional(email.Date),
                    dedupe = string.IsNullOrWhiteSpace(messageId) ? "content_hash" : "message_id",
                }),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        return rows;
    }

    private static bool RequiresReview(ExpenseParseResult parsed)
        => !parsed.ConfidenceScore.HasValue ||
           parsed.ConfidenceScore.Value < AutoApprovalConfidenceThreshold;

    private static string BuildReviewReason(ExpenseParseResult parsed)
        => parsed.ConfidenceScore.HasValue
            ? LowConfidenceReviewReason
            : MissingConfidenceReviewReason;

    private static bool IsDuplicate(
        StoredTransaction row,
        ISet<string> existingMessageIds,
        ISet<string> existingContentHashes)
    {
        if (!string.IsNullOrWhiteSpace(row.MessageId))
            return existingMessageIds.Contains(row.MessageId);

        return !string.IsNullOrWhiteSpace(row.ContentHash) &&
               existingContentHashes.Contains(row.ContentHash);
    }

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
        var parsed = TryParseDateTimeOffset(rawDate);
        return parsed.HasValue ? DateOnly.FromDateTime(parsed.Value.UtcDateTime) : null;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
            return null;

        var value = NormalizeDate(rawDate);
        if (value.Contains('/'))
        {
            if (DateTimeOffset.TryParseExact(
                    value,
                    new[] { "dd/MM/yyyy", "d/M/yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var dayFirstParsed))
            {
                return dayFirstParsed.ToUniversalTime();
            }
        }

        var supportedFormats = new[]
        {
            "dd/MM/yyyy",
            "d/M/yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
        };
        if (DateTimeOffset.TryParseExact(
                value,
                supportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsedExact))
        {
            return parsedExact.ToUniversalTime();
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedGeneric))
            return parsedGeneric.ToUniversalTime();

        if (DateTimeOffset.TryParse(value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out parsedGeneric))
            return parsedGeneric.ToUniversalTime();

        return null;
    }

    private static string NormalizeDate(string rawDate)
    {
        var value = rawDate.Trim();
        value = Regex.Replace(value, @"\s*\([A-Za-z]{2,8}\)\s*$", string.Empty);
        value = Regex.Replace(value, @"([+-]\d{2})(\d{2})\b", "$1:$2");
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value;
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
        if (string.Equals(normalized, NonTransactionCategory, StringComparison.OrdinalIgnoreCase))
            return null;

        var currencyIndex = normalized.IndexOf("(", StringComparison.Ordinal);
        var merchant = currencyIndex > 0
            ? normalized[..currencyIndex].Trim()
            : normalized;

        return string.IsNullOrWhiteSpace(merchant) ? null : merchant;
    }

    private static string BuildContentHash(EmailEntry email)
    {
        var material = string.Join(
            '\n',
            NormalizeOptional(email.Date) ?? string.Empty,
            NormalizeOptional(email.Sender) ?? string.Empty,
            NormalizeOptional(email.Subject) ?? string.Empty,
            NormalizeOptional(email.Message) ?? string.Empty);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SerializeJson<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);
}

public sealed record TransactionPersistenceResult(int CreatedCount, int DuplicateCount);
