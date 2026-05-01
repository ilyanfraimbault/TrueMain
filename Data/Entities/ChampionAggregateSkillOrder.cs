namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> and the
/// API skill-order aggregator. No longer an EF entity since Phase 6.4
/// dropped the per-scope <c>champion_aggregate_skill_orders</c> table.
/// </summary>
public class ChampionAggregateSkillOrder
{
    public string SkillOrderKey { get; set; } = string.Empty;

    public int Games { get; set; }
    public int Wins { get; set; }
}
