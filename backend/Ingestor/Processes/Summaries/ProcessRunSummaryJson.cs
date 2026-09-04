using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ingestor.Processes.Summaries;

/// <summary>
/// Source-generated metadata for every <see cref="IProcessRunSummary"/>. Every
/// implementation must be registered here — the resolver is source-gen only, so
/// an unregistered type throws <see cref="NotSupportedException"/> at serialization
/// time (guarded by <c>ProcessRunSummaryRegistrationTests</c>).
/// </summary>
[JsonSerializable(typeof(NoWorkSummary))]
[JsonSerializable(typeof(SkippedSummary))]
[JsonSerializable(typeof(HarvestNoWorkSummary))]
[JsonSerializable(typeof(ManualSeedNoWorkSummary))]
[JsonSerializable(typeof(ChampionPatternNoWorkSummary))]
[JsonSerializable(typeof(ScoringSummary))]
[JsonSerializable(typeof(DiscoverySummary))]
[JsonSerializable(typeof(MatchIngestionSummary))]
[JsonSerializable(typeof(ManualSeedSummary))]
[JsonSerializable(typeof(HarvestSummary))]
[JsonSerializable(typeof(AccountRefreshSummary))]
[JsonSerializable(typeof(LadderSyncSummary))]
[JsonSerializable(typeof(MainAnalysisSummary))]
[JsonSerializable(typeof(MainActivitySummary))]
[JsonSerializable(typeof(ChampionPatternAggregationSummary))]
[JsonSerializable(typeof(EloBracketEnrichmentSummary))]
[JsonSerializable(typeof(TeamPositionCorrectionSummary))]
[JsonSerializable(typeof(MatchAggregationSummary))]
[JsonSerializable(typeof(SynergyAggregationSummary))]
[JsonSerializable(typeof(BanAggregationSummary))]
[JsonSerializable(typeof(ChampionProfileAggregationSummary))]
[JsonSerializable(typeof(ChampionItemContextAggregationSummary))]
[JsonSerializable(typeof(StorageSnapshotSummary))]
[JsonSerializable(typeof(CandidateStockSnapshotSummary))]
[JsonSerializable(typeof(MatchupAggregationSummary))]
[JsonSerializable(typeof(MatchDataRetentionSummary))]
public sealed partial class ProcessRunSummaryJsonContext : JsonSerializerContext;

/// <summary>
/// The single serializer used for persisted process-run summaries.
/// </summary>
public static class ProcessRunSummaryJson
{
    /// <summary>
    /// Pins the emitted property names to the shape the anonymous summaries used
    /// to produce. Those anonymous types declared their members in camelCase and
    /// were serialized with default options (no naming policy), so the wire names
    /// were the camelCase member names verbatim; camelCase-ing the PascalCase
    /// record properties reproduces them exactly. The naming policy has to live on
    /// the options rather than on <c>[JsonSourceGenerationOptions]</c>: when a
    /// context is attached through <see cref="JsonSerializerOptions.TypeInfoResolver"/>
    /// the options in force at call time win over the context's own attribute.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = ProcessRunSummaryJsonContext.Default
    };

    /// <summary>
    /// Serializes a summary straight to a <see cref="JsonDocument"/> for the
    /// jsonb column — no intermediate string, and no reflection.
    /// </summary>
    /// <remarks>
    /// The runtime type is passed explicitly: the static type here is the
    /// <see cref="IProcessRunSummary"/> marker, and serializing against an
    /// interface contract would emit an empty object.
    /// </remarks>
    public static JsonDocument ToDocument(IProcessRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.SerializeToDocument(summary, summary.GetType(), Options);
    }

    /// <summary>Serializes a summary to its persisted JSON text.</summary>
    public static string Serialize(IProcessRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.Serialize(summary, summary.GetType(), Options);
    }
}
