namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> and the
/// API summoner-spell aggregator. No longer an EF entity since Phase 6.4
/// dropped the per-scope <c>champion_aggregate_spell_pairs</c> table.
/// </summary>
public class ChampionAggregateSpellPair
{
    public int Spell1Id { get; set; }
    public int Spell2Id { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }
}
