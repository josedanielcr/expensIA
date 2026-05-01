public sealed class SyncRun
{
    public Guid Id { get; set; }

    public string OwnerGoogleSub { get; set; } = string.Empty;

    public string OwnerEmail { get; set; } = string.Empty;

    public SyncRunStatus Status { get; set; } = SyncRunStatus.Running;

    public int EmailsReceivedCount { get; set; }

    public int EmailsProcessedCount { get; set; }

    public int TransactionsCreatedCount { get; set; }

    public int DuplicatesCount { get; set; }

    public int PendingReviewCount { get; set; }

    public int ApprovedCount { get; set; }

    public int SheetReadyCount { get; set; }

    public int SheetSyncedCount { get; set; }

    public int ErrorCount { get; set; }

    public string ErrorsJson { get; set; } = "[]";

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<StoredTransaction> Transactions { get; } = [];
}
