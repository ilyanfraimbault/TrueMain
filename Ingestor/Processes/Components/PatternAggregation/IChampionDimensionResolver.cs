using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

/// <summary>
/// Phase 6.2 — get-or-create the 5 deduplicated dimension rows for a batch
/// of patterns. Returns content → ID lookups so the persister can stamp
/// FKs on each <see cref="ChampionAggregatePattern"/> row.
/// </summary>
public interface IChampionDimensionResolver
{
    Task<DimensionResolution> ResolveAsync(
        IReadOnlyCollection<PatternIntent> patterns,
        CancellationToken ct);
}

public sealed record DimensionResolution(
    IReadOnlyDictionary<BuildDimensionContent, Guid> Builds,
    IReadOnlyDictionary<RunePageDimensionContent, Guid> RunePages,
    IReadOnlyDictionary<string, Guid> SkillOrders,
    IReadOnlyDictionary<SpellPairDimensionContent, Guid> SpellPairs,
    IReadOnlyDictionary<string, Guid> StarterItems);
