using System.Text.Json;

namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Health and coverage snapshot of every aggregation pipeline for the admin
/// Aggregation panel: per-family exact row counts, champion/patch coverage,
/// data freshness, the latest recorded run, and the ingestion backlogs that
/// should sit at zero when the pipeline is caught up.
/// </summary>
public sealed record AggregationsReadModel
{
    /// <summary>Queue the pipeline aggregates (soloq); backlog counts are scoped to it.</summary>
    public int QueueId { get; init; }

    public IReadOnlyList<AggregationFamilyReadModel> Families { get; init; } = [];

    public AggregationBacklogReadModel Backlog { get; init; } = new();
}

/// <summary>
/// One aggregation family (builds patterns, matchups, timeline leads,
/// powerspikes, mains): the tables it owns with exact row counts, its
/// champion/patch coverage, and the latest run of the process producing it.
/// </summary>
public sealed record AggregationFamilyReadModel
{
    /// <summary>Stable identifier for the frontend ("builds", "matchups", ...).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The recorded ingestor process producing this family.</summary>
    public string ProcessName { get; init; } = string.Empty;

    public IReadOnlyList<AggregationTableCountReadModel> Tables { get; init; } = [];

    public long TotalRows { get; init; }

    public int DistinctChampions { get; init; }

    /// <summary>Distinct normalized patches covered; null when the family has no patch axis.</summary>
    public int? DistinctPatches { get; init; }

    /// <summary>Most recent aggregate-row write timestamp — data freshness independent of run records.</summary>
    public DateTime? LastAggregatedAtUtc { get; init; }

    /// <summary>Latest recorded run of the producing process; null when it never ran.</summary>
    public AggregationRunReadModel? LastRun { get; init; }
}

public sealed record AggregationTableCountReadModel
{
    public string Table { get; init; } = string.Empty;

    public long Rows { get; init; }
}

/// <summary>
/// Rollup of the producing process's <c>process_runs</c> rows: the latest run's
/// outcome plus the last time it succeeded (they differ when the latest run
/// failed). <see cref="LastSuccessSummary"/> is the JSONB payload the process
/// returned on its last success (e.g. per-run row/champion counts).
/// </summary>
public sealed record AggregationRunReadModel
{
    public string Status { get; init; } = string.Empty;

    public DateTime? LastStartedAtUtc { get; init; }

    public DateTime? LastFinishedAtUtc { get; init; }

    public DateTime? LastSuccessAtUtc { get; init; }

    public int? DurationMs { get; init; }

    public JsonElement? LastSuccessSummary { get; init; }
}

/// <summary>
/// Work waiting on the aggregation side of the pipeline. Both counters read
/// zero when aggregations are caught up with ingestion.
/// </summary>
public sealed record AggregationBacklogReadModel
{
    /// <summary>
    /// Queue-scoped matches with an ingested timeline not yet folded into the
    /// powerspike aggregates (the only per-match-flagged aggregation).
    /// </summary>
    public long PendingPowerspikeMatches { get; init; }

    /// <summary>
    /// Tracked participants still missing their elo bracket stamp — rows the
    /// rank-scoped aggregations cannot bucket yet.
    /// </summary>
    public long PendingEloBracketParticipants { get; init; }

    /// <summary>Queue-scoped matches with an ingested timeline (backlog denominator).</summary>
    public long TimelineIngestedMatches { get; init; }
}
