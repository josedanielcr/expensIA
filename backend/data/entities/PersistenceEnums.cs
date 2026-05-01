public enum SyncRunStatus
{
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
}

public enum TransactionReviewStatus
{
    PendingReview,
    Approved,
    Corrected,
}

public enum SheetSyncStatus
{
    NotReady,
    Ready,
    SheetSynced,
    Failed,
}

public enum ReviewEventType
{
    Approved,
    Corrected,
    MarkedSheetSynced,
    SheetSyncFailed,
    RuleApplied,
}

public enum MerchantRuleStatus
{
    Candidate,
    Active,
    Disabled,
}

public enum MerchantRuleMatchType
{
    Exact,
    Contains,
    Regex,
}
