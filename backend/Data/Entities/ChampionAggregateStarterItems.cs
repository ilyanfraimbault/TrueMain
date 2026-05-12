namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> and the
/// API starter-items aggregator. No longer an EF entity since Phase 6.4
/// dropped the per-scope <c>champion_aggregate_starter_items</c> table.
/// </summary>
public class ChampionAggregateStarterItems
{
    public string StarterItemsKey { get; set; } = string.Empty;
    public List<int> StarterItems { get; set; } = [];

    public int Games { get; set; }
    public int Wins { get; set; }
}
