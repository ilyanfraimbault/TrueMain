namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> (which
/// builds it from <see cref="ChampionAggregatePattern"/> + <see cref="ChampionDimBuild"/>)
/// and the per-dimension aggregators in the API. Not an EF entity — it has
/// no backing table and exists only as the aggregators' input shape.
/// </summary>
public class ChampionAggregateBuild
{
    public int BootsItemId { get; set; }
    public int BuildItem0 { get; set; }
    public int BuildItem1 { get; set; }
    public int BuildItem2 { get; set; }
    public int BuildItem3 { get; set; }
    public int BuildItem4 { get; set; }
    public int BuildItem5 { get; set; }
    public int BuildItem6 { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }
}
