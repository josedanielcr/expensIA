using System.Text.Json.Serialization;

public sealed class ReviewTransactionActionRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "approve";

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("sheetRowId")]
    public string SheetRowId { get; set; } = string.Empty;
}
