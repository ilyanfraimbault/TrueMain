namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> and the
/// API starter-items aggregator. Not an EF entity — it has no backing
/// table and exists only as the aggregator's input shape.
/// </summary>
public class ChampionAggregateStarterItems
{
    public string StarterItemsKey { get; set; } = string.Empty;
    public List<int> StarterItems { get; set; } = [];

    public int Games { get; set; }
    public int Wins { get; set; }
}
