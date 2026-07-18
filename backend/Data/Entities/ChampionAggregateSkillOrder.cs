namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> and the
/// API skill-order aggregator. Not an EF entity — it has no backing
/// table and exists only as the aggregator's input shape.
/// </summary>
public class ChampionAggregateSkillOrder
{
    public string SkillOrderKey { get; set; } = string.Empty;

    public int Games { get; set; }
    public int Wins { get; set; }
}
