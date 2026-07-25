using System.Text.Json.Serialization;

namespace Data.Entities;

/// <summary>
/// One item timeline event of a <see cref="MatchParticipant"/> (purchase, sale,
/// undo, destruction), persisted inside the <c>ItemEvents</c> <c>jsonb</c> column
/// by Npgsql dynamic JSON.
/// </summary>
/// <remarks>
/// The <see cref="JsonPropertyNameAttribute"/> on every property pins the wire
/// shape of the stored documents to the PascalCase keys already written in
/// production (<c>{"ItemId":…,"AfterId":…,"BeforeId":…,"EventType":…,"TimestampMs":…}</c>).
/// Without them, renaming a property — or configuring a naming policy on the shared
/// <c>JsonSerializerOptions</c> of the data source built in
/// <see cref="DataServiceCollectionExtensions.BuildDataSource"/> — would silently
/// make every existing row deserialize to defaults instead of failing loudly.
/// The raw-SQL reads that unnest this column (for example
/// <c>Api/Services/Champions/ChampionItemTimingsQueryService</c>, which filters on
/// <c>ev-&gt;&gt;'EventType'</c> and <c>ev-&gt;&gt;'ItemId'</c>) depend on the very
/// same keys.
/// </remarks>
public class ItemEvent
{
    /// <summary>Milliseconds since the start of the game.</summary>
    [JsonPropertyName("TimestampMs")]
    public int TimestampMs { get; set; }

    /// <summary>Riot timeline event type, for example <c>ITEM_PURCHASED</c>.</summary>
    [JsonPropertyName("EventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Item involved in the event; <c>0</c> for undo events that carry only before/after ids.</summary>
    [JsonPropertyName("ItemId")]
    public int ItemId { get; set; }

    /// <summary>Item held before an <c>ITEM_UNDO</c>; <see langword="null"/> otherwise.</summary>
    [JsonPropertyName("BeforeId")]
    public int? BeforeId { get; set; }

    /// <summary>Item held after an <c>ITEM_UNDO</c>; <see langword="null"/> otherwise.</summary>
    [JsonPropertyName("AfterId")]
    public int? AfterId { get; set; }
}
