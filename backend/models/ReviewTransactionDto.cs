using System.Text.Json.Serialization;

public sealed class ReviewTransactionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("confidence_score")]
    public decimal? ConfidenceScore { get; set; }

    [JsonPropertyName("review_reason")]
    public string ReviewReason { get; set; } = string.Empty;

    [JsonPropertyName("review_status")]
    public string ReviewStatus { get; set; } = string.Empty;

    [JsonPropertyName("sheet_sync_status")]
    public string SheetSyncStatus { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;
}
