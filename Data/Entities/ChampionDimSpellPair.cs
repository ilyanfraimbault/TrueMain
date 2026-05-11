namespace Data.Entities;

/// <summary>
/// Globally-deduplicated reference row for one unique summoner-spell pair
/// (Spell1, Spell2). Phase 6 dimension table — referenced by
/// <see cref="ChampionAggregatePattern"/>. Pairs are stored in the canonical
/// order produced by <c>SummonerSpellPair</c> so equivalent pairs collapse
/// to one row.
/// </summary>
public sealed class ChampionDimSpellPair
{
    public Guid Id { get; set; }

    public int Spell1Id { get; set; }
    public int Spell2Id { get; set; }
}
