using Data.Entities;

namespace TrueMain.Services.Champions;

/// <summary>
/// Phase 6.3 — projection of <see cref="ChampionAggregatePattern"/> rows into
/// the per-dimension aggregate shapes the existing aggregators consume.
/// Each list re-uses the legacy entity types as a transport, but the
/// instances are unattached POCOs (<c>Id = Guid.Empty</c>, no
/// <c>ScopeId</c>) — the consuming aggregators never look at those fields.
/// Switching the source from the per-scope dim tables to the pattern
/// junction here is what unlocks cross-dim correlation: when a pivot is
/// supplied, every list reflects only the patterns that match the pivot.
/// </summary>
internal sealed record ChampionPatternProjection(
    IReadOnlyList<ChampionAggregateBuild> Builds,
    IReadOnlyList<ChampionAggregateRunePage> RunePages,
    IReadOnlyList<ChampionAggregateSkillOrder> SkillOrders,
    IReadOnlyList<ChampionAggregateSpellPair> SpellPairs,
    IReadOnlyList<ChampionAggregateStarterItems> StarterItems)
{
    public static ChampionPatternProjection Empty { get; } = new(
        Builds: [],
        RunePages: [],
        SkillOrders: [],
        SpellPairs: [],
        StarterItems: []);
}

/// <summary>
/// Optional pivot for cross-dimension correlation queries. When all
/// properties are <c>null</c> the projector returns the overall per-scope
/// aggregates (same shape the legacy dim tables provided). When one is
/// set, the patterns are filtered to that pivot first, then aggregated —
/// answering questions like "what runes do players run with this build".
/// </summary>
public sealed record ChampionPatternPivot(
    Guid? BuildId = null)
{
    public static ChampionPatternPivot None { get; } = new();

    public bool IsEmpty => BuildId is null;
}
