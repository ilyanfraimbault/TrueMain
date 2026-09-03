namespace Ingestor.Processes.Components.PatternAggregation;

/// <summary>
/// Value-equality wrappers for the content of each Phase 6 dimension. Used
/// as dictionary keys and as the natural-key tuples that
/// <see cref="IChampionDimensionResolver"/> resolves to dim row IDs.
/// </summary>
public sealed record BuildDimensionContent(
    int BootsItemId,
    int BuildItem0,
    int BuildItem1,
    int BuildItem2,
    int BuildItem3,
    int BuildItem4,
    int BuildItem5,
    int BuildItem6);

public sealed record RunePageDimensionContent(
    int PrimaryStyleId,
    int PrimaryKeystoneId,
    int PrimaryPerk1Id,
    int PrimaryPerk2Id,
    int PrimaryPerk3Id,
    int SecondaryStyleId,
    int SecondaryPerk1Id,
    int SecondaryPerk2Id,
    int StatOffense,
    int StatFlex,
    int StatDefense)
{
    /// <summary>
    /// The secondary pair, sorted — the same canonical form the dimension's UNIQUE index
    /// and CHECK enforce (#911, #1418). Normalising in the content type rather than at the
    /// call sites is what keeps the in-memory get-or-create key and the database's notion
    /// of identity the same thing: a caller that passes the player's click order gets the
    /// canonical row back instead of minting a second one the database would then reject.
    /// </summary>
    public int SecondaryPerk1Id { get; } = Math.Min(SecondaryPerk1Id, SecondaryPerk2Id);

    /// <inheritdoc cref="SecondaryPerk1Id"/>
    public int SecondaryPerk2Id { get; } = Math.Max(SecondaryPerk1Id, SecondaryPerk2Id);
}

public sealed record SpellPairDimensionContent(
    int Spell1Id,
    int Spell2Id)
{
    /// <summary>
    /// The pair, sorted. A loadout is a set: Flash+Ignite and Ignite+Flash are one row,
    /// and the dimension's CHECK now says so.
    /// </summary>
    public int Spell1Id { get; } = Math.Min(Spell1Id, Spell2Id);

    /// <inheritdoc cref="Spell1Id"/>
    public int Spell2Id { get; } = Math.Max(Spell1Id, Spell2Id);
}

/// <summary>
/// One pattern observed inside a scope: the (scope, build, runes, skill,
/// spells, starters) tuple and its games/wins counts. Carried through
/// the aggregation pipeline from
/// <see cref="ChampionPatternAggregateBuilder"/> to
/// <see cref="ChampionPatternAggregatePersister"/>; the persister resolves
/// dim contents to FK IDs via <see cref="IChampionDimensionResolver"/>
/// before insertion.
/// </summary>
public sealed record PatternIntent(
    Guid ScopeId,
    BuildDimensionContent Build,
    RunePageDimensionContent RunePage,
    string SkillOrderKey,
    SpellPairDimensionContent SpellPair,
    string StarterItemsKey,
    IReadOnlyList<int> StarterItems,
    int Games,
    int Wins);
