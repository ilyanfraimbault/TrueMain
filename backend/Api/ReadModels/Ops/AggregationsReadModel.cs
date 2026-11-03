using System.Text.Json;

namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Health and coverage snapshot of every aggregation pipeline for the admin
/// Aggregation panel: per-family row estimates, champion coverage, the latest
/// recorded run, and the ingestion backlogs that should sit at zero when the
/// pipeline is caught up.
///
/// <para>
/// Every figure is a catalog lookup or an index probe (#2303): exact counts,
/// <c>DISTINCT</c>s and <c>max()</c>s over the fold tables were full scans that
/// took the page past a minute on a cold cache.
/// </para>
/// </summary>
public sealed record AggregationsReadModel
{
    /// <summary>Queue the pipeline aggregates (soloq); backlog counts are scoped to it.</summary>
    public int QueueId { get; init; }

    public IReadOnlyList<AggregationFamilyReadModel> Families { get; init; } = [];

    public AggregationBacklogReadModel Backlog { get; init; } = new();

    /// <summary>When this payload was measured — it is served from a short cache.</summary>
    public DateTime MeasuredAtUtc { get; init; }
}

/// <summary>
/// One aggregation family (builds patterns, matchups, synergies, mains):
/// the tables it owns with their planner row estimates, its champion coverage,
/// and the latest run of the process producing it.
/// </summary>
public sealed record AggregationFamilyReadModel
{
    /// <summary>Stable identifier for the frontend ("builds", "matchups", ...).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The recorded ingestor process producing this family.</summary>
    public string ProcessName { get; init; } = string.Empty;

    public IReadOnlyList<AggregationTableCountReadModel> Tables { get; init; } = [];

    /// <summary>Sum of the tables' planner estimates — approximate, like each of them.</summary>
    public long TotalRowEstimate { get; init; }

    /// <summary>
    /// Distinct champions the family holds rows for, exact (an index skip scan). For mains,
    /// the champions with at least one active main.
    /// </summary>
    public int DistinctChampions { get; init; }

    /// <summary>Latest recorded run of the producing process; null when it never ran.</summary>
    public AggregationRunReadModel? LastRun { get; init; }
}

public sealed record AggregationTableCountReadModel
{
    public string Table { get; init; } = string.Empty;

    /// <summary>
    /// The planner's live-row estimate (<c>pg_stat_user_tables.n_live_tup</c>, the figure
    /// <c>/database</c> prints), falling back to <c>pg_class.reltuples</c> when the statistics
    /// view has no count yet.
    /// </summary>
    public long RowEstimate { get; init; }
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
/// Work waiting on the aggregation side of the pipeline. Every counter reads
/// zero when aggregations are caught up with ingestion.
/// </summary>
public sealed record AggregationBacklogReadModel
{
    /// <summary>
    /// Queue-scoped matches not yet folded into the synergy aggregates (#922).
    /// It has no timeline prerequisite, and it
    /// starts at the full retained match count: the fold flag ships false for every
    /// pre-existing row on purpose, so this reads as a large backlog on the first
    /// deploy and drains over the following runs.
    /// </summary>
    public long PendingSynergyMatches { get; init; }

    /// <summary>
    /// Tracked participants still missing their elo bracket stamp — rows the
    /// rank-scoped aggregations cannot bucket yet. Served by the partial index
    /// <c>IX_match_participants_elo_bracket_pending</c> (#2303). It does not drain to
    /// zero on its own: rows of accounts without a rank snapshot stay pending until
    /// the account's first snapshot arrives.
    /// </summary>
    public long PendingEloBracketParticipants { get; init; }
}
