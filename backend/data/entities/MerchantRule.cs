public sealed class MerchantRule
{
    public Guid Id { get; set; }

    public string OwnerGoogleSub { get; set; } = string.Empty;

    public string OwnerEmail { get; set; } = string.Empty;

    public MerchantRuleMatchType MatchType { get; set; } = MerchantRuleMatchType.Contains;

    public string MatchValue { get; set; } = string.Empty;

    public string? MerchantName { get; set; }

    public string Category { get; set; } = string.Empty;

    public MerchantRuleStatus Status { get; set; } = MerchantRuleStatus.Candidate;

    public int CorrectionCount { get; set; } = 1;

    public int ActivationThreshold { get; set; } = 3;

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }

    public DateTimeOffset? LastAppliedAt { get; set; }

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ReviewEvent> ReviewEvents { get; } = [];
}
