namespace Data.Entities;

/// <summary>
/// Phase 6 junction row: one combo (build + runes + skill order + spells +
/// starters) actually observed inside a given
/// <see cref="ChampionAggregateScope"/>. Counts live here, not on the
/// dimension tables — summing <see cref="Games"/> by any FK gives the
/// per-dimension total without double-counting (each match contributes to
/// exactly one pattern row).
/// </summary>
public sealed class ChampionAggregatePattern
{
    public Guid Id { get; set; }

    public Guid ScopeId { get; set; }
    public ChampionAggregateScope Scope { get; set; } = null!;

    public Guid BuildId { get; set; }
    public ChampionDimBuild Build { get; set; } = null!;

    public Guid RunePageId { get; set; }
    public ChampionDimRunePage RunePage { get; set; } = null!;

    public Guid SkillOrderId { get; set; }
    public ChampionDimSkillOrder SkillOrder { get; set; } = null!;

    public Guid SpellPairId { get; set; }
    public ChampionDimSpellPair SpellPair { get; set; } = null!;

    public Guid StarterItemsId { get; set; }
    public ChampionDimStarterItems StarterItems { get; set; } = null!;

    public int Games { get; set; }
    public int Wins { get; set; }
}
