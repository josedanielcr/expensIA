public sealed class ReviewEvent
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }

    public Guid? MerchantRuleId { get; set; }

    public string OwnerGoogleSub { get; set; } = string.Empty;

    public string OwnerEmail { get; set; } = string.Empty;

    public string? ActorGoogleSub { get; set; }

    public string? ActorEmail { get; set; }

    public ReviewEventType EventType { get; set; }

    public string PreviousValuesJson { get; set; } = "{}";

    public string NewValuesJson { get; set; } = "{}";

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public StoredTransaction? Transaction { get; set; }

    public MerchantRule? MerchantRule { get; set; }
}
