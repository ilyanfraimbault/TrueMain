namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> and the
/// API summoner-spell aggregator. Not an EF entity — it has no backing
/// table and exists only as the aggregator's input shape.
/// </summary>
public class ChampionAggregateSpellPair
{
    public int Spell1Id { get; set; }
    public int Spell2Id { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }
}
