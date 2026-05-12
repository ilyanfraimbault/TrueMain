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
    int StatDefense);

public sealed record SpellPairDimensionContent(
    int Spell1Id,
    int Spell2Id);

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
