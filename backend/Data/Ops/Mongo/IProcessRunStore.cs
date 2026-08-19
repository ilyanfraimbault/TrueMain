using Data.Entities;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo-backed store for recorded ingestor process runs (the <c>process_runs</c>
/// collection). Writes come from the Ingestor's <c>ProcessRunRecorder</c> (plus
/// the Discovery cadence read); the query methods serve the admin process panels.
/// Like the rest of the observability store, everything degrades to a no-op /
/// empty result when Mongo is not configured — process runs are operator-facing
/// telemetry, and a missing Mongo must never take the pipeline down.
/// </summary>
public interface IProcessRunStore
{
    /// <summary>Inserts a new run document (the Running start row, or a recovered terminal row).</summary>
    Task InsertAsync(ProcessRunDocument run, CancellationToken ct);

    /// <summary>
    /// Finalises the in-flight run in place (finish time, duration, terminal
    /// status, error, summary). Returns false when no document matched
    /// <paramref name="id"/> — the caller then records a fresh terminal document so
    /// the outcome is never lost. Returns true without writing when the store is
    /// inactive.
    /// </summary>
    Task<bool> FinalizeAsync(
        Guid id,
        DateTime finishedAtUtc,
        int durationMs,
        ProcessRunStatus status,
        string? error,
        string? summaryJson,
        CancellationToken ct);

    /// <summary>
    /// Refreshes the liveness heartbeat of an in-flight run. Guarded on
    /// <see cref="ProcessRunStatus.Running"/> so a finished (or reaped) run is
    /// never resurrected as "fresh".
    /// </summary>
    Task TouchHeartbeatAsync(Guid id, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Flips every still-Running document to <see cref="ProcessRunStatus.Abandoned"/>
    /// with a real finish time and duration. Called at ingestor startup — the
    /// single-instance ingestor owns every in-flight run, so anything Running at
    /// boot died with the previous process. Returns the number of runs abandoned.
    /// </summary>
    Task<int> AbandonRunningAsync(DateTime finishedAtUtc, string error, CancellationToken ct);

    /// <summary>
    /// The start time of the most recent run of <paramref name="processName"/> that
    /// actually did its work, or null when none exists — the Discovery cadence gate.
    /// <para>
    /// <see cref="ProcessRunStatus.Running"/> and <see cref="ProcessRunStatus.Skipped"/>
    /// are both excluded: the former is the caller's own in-flight row, the latter is a
    /// run that deliberately did nothing. Counting a skip here would let it stand in for
    /// the work it declined to do, re-arming the guard on every iteration (#1149).
    /// </para>
    /// </summary>
    Task<DateTime?> GetLastCompletedRunStartAsync(string processName, CancellationToken ct);

    /// <summary>
    /// One page of runs, newest-first (started desc, id desc), with the total
    /// count of the filtered set. Filters are optional: exact process name, exact
    /// raw status, and a started-at lower bound.
    /// </summary>
    Task<ProcessRunPage> QueryRunsAsync(
        string? processName,
        ProcessRunStatus? status,
        DateTime? since,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// The process name, start time and raw summary JSON of every run of any of
    /// <paramref name="processNames"/> started at or after <paramref name="sinceUtc"/>,
    /// oldest first. Projects only those three fields, because the caller needs the
    /// summary's counters and nothing else, and 180 days of a back-to-back pipeline is
    /// a lot of documents to hydrate whole.
    /// </summary>
    /// <remarks>
    /// The summary stays a string here: it is stored as opaque JSON text (see
    /// <see cref="ProcessRunDocument.SummaryJson"/>), so Mongo cannot sum a counter
    /// inside it and the caller parses. What the server still does is the part it is
    /// good at — the indexed <c>(processName, startedAtUtc)</c> range scan.
    ///
    /// <para>
    /// Several names in one call because the candidate funnel (#1024) draws one series
    /// from six different processes and would otherwise fan out into six round trips
    /// over the same index for the same window.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ProcessRunSummarySample>> GetRunSummariesAsync(
        IReadOnlyCollection<string> processNames,
        DateTime sinceUtc,
        CancellationToken ct);

    /// <summary>
    /// Per-process rollup over the (optionally name-filtered) whole collection:
    /// the latest run's raw status + heartbeat, last run start, last successful
    /// finish, and run/failure counts within the window
    /// (<paramref name="windowStart"/> null = unbounded, true all-time totals).
    /// Ordered by process name.
    /// </summary>
    Task<IReadOnlyList<ProcessRunRollup>> GetRollupsAsync(
        string? processName,
        DateTime? windowStart,
        CancellationToken ct);

    /// <summary>
    /// One page of iteration headers (iteration-stamped runs grouped by
    /// <see cref="ProcessRunDocument.IterationId"/>), newest-first by the pass
    /// start. With <paramref name="finishedOnly"/> an iteration that still has a
    /// Running run with a heartbeat at or after
    /// <paramref name="freshHeartbeatCutoff"/> is excluded from the page and the
    /// total.
    /// </summary>
    Task<ProcessIterationHeaderPage> QueryIterationsAsync(
        int page,
        int pageSize,
        bool finishedOnly,
        DateTime freshHeartbeatCutoff,
        CancellationToken ct);

    /// <summary>Every run of the given iterations, ordered by start asc then id.</summary>
    Task<IReadOnlyList<ProcessRunDocument>> GetRunsForIterationsAsync(
        IReadOnlyCollection<Guid> iterationIds,
        CancellationToken ct);

    /// <summary>
    /// The newest run per process (by finish time) among
    /// <paramref name="processNames"/> — optionally the newest <em>successful</em>
    /// run. Processes with no matching run are simply absent.
    /// </summary>
    Task<IReadOnlyList<ProcessRunDocument>> GetLatestPerProcessAsync(
        IReadOnlyCollection<string> processNames,
        bool onlySuccesses,
        CancellationToken ct);

    /// <summary>
    /// How many terminal (non-Running) runs of <paramref name="processName"/> started
    /// strictly after <paramref name="afterUtc"/>; null counts every terminal run.
    /// </summary>
    /// <remarks>
    /// Pass the process's last successful finish and the count <em>is</em> its current
    /// failure streak — every terminal run since a success is by definition not one. Kept a
    /// count rather than folded into <see cref="GetRollupsAsync"/> because the streak needs
    /// the last-success timestamp that rollup computes in the same <c>$group</c>, and
    /// because the cockpit (#1031) only asks it of processes whose latest run did not
    /// succeed — usually none, so the healthy case costs nothing.
    /// </remarks>
    Task<long> CountTerminalRunsSinceAsync(string processName, DateTime? afterUtc, CancellationToken ct);
}

/// <summary>
/// One run reduced to what a counter series needs: which process it was, when it
/// started, and the raw summary JSON its counters live in. <see cref="SummaryJson"/>
/// is null for a run that recorded none — a failure, an abandoned run, or one still
/// in flight.
/// </summary>
public sealed record ProcessRunSummarySample(string ProcessName, DateTime StartedAtUtc, string? SummaryJson);

/// <summary>One page of <see cref="ProcessRunDocument"/>s plus the filtered total.</summary>
public sealed record ProcessRunPage(IReadOnlyList<ProcessRunDocument> Runs, long Total);

/// <summary>
/// Per-process rollup row for the admin runs panel. Statuses are raw — the read
/// service maps a stale-heartbeat Running latest run to Abandoned.
/// </summary>
public sealed record ProcessRunRollup(
    string ProcessName,
    ProcessRunStatus LatestStatus,
    DateTime? LatestHeartbeatAtUtc,
    DateTime LastRunAtUtc,
    DateTime? LastSuccessAtUtc,
    long RunCountInWindow,
    long FailureCountInWindow);

/// <summary>One iteration header (grouping key + pass start) for the chain view.</summary>
public sealed record ProcessIterationHeader(Guid IterationId, DateTime StartedAtUtc);

/// <summary>One page of iteration headers plus the filtered total.</summary>
public sealed record ProcessIterationHeaderPage(IReadOnlyList<ProcessIterationHeader> Headers, long Total);
