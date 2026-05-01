public sealed class StoredTransaction
{
    public Guid Id { get; set; }

    public string OwnerGoogleSub { get; set; } = string.Empty;

    public string OwnerEmail { get; set; } = string.Empty;

    public Guid? SyncRunId { get; set; }

    public string? MessageId { get; set; }

    public string? ContentHash { get; set; }

    public string? Subject { get; set; }

    public string? EmailFrom { get; set; }

    public DateTimeOffset? EmailReceivedAt { get; set; }

    public string? Merchant { get; set; }

    public string? Description { get; set; }

    public string? Category { get; set; }

    public DateOnly? TransactionDate { get; set; }

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public decimal? OriginalAmount { get; set; }

    public string? OriginalCurrency { get; set; }

    public decimal? ExchangeRate { get; set; }

    public decimal? ConfidenceScore { get; set; }

    public TransactionReviewStatus ReviewStatus { get; set; } = TransactionReviewStatus.Approved;

    public string? ReviewReason { get; set; }

    public SheetSyncStatus SheetSyncStatus { get; set; } = SheetSyncStatus.Ready;

    public DateTimeOffset? SheetSyncedAt { get; set; }

    public string? SheetRowId { get; set; }

    public string? SheetError { get; set; }

    public string? ParserVersion { get; set; }

    public string? ParserModel { get; set; }

    public DateTimeOffset ParsedAt { get; set; } = DateTimeOffset.UtcNow;

    public string ParsedPayloadJson { get; set; } = "{}";

    public string EmailMetadataJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SyncRun? SyncRun { get; set; }

    public ICollection<ReviewEvent> ReviewEvents { get; } = [];
}
