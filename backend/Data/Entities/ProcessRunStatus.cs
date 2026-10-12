namespace Data.Entities;

/// <summary>
/// Lifecycle of a recorded ingestor process run (<see cref="Data.Ops.Mongo.ProcessRunDocument"/>).
/// </summary>
public enum ProcessRunStatus
{
    Success = 0,
    Failed = 1,

    /// <summary>
    /// The process has started and is still in flight. A <c>Running</c> row is
    /// written when the process begins and updated to <see cref="Success"/> or
    /// <see cref="Failed"/> on completion. A row left in this state (e.g. after a
    /// host crash) reads as stale-running rather than a recorded outcome.
    /// </summary>
    Running = 2,

    /// <summary>
    /// A <see cref="Running"/> row whose owner died: detected at ingestor startup
    /// (the single-instance ingestor reconciles every still-<c>Running</c> row to
    /// this state, since nothing it owns can survive a restart), or inferred by
    /// read queries from a stale (or missing) <c>LastHeartbeatAtUtc</c>. Surfaces
    /// the run as never-completed instead of perpetually in-flight.
    /// </summary>
    Abandoned = 3,

    /// <summary>
    /// The process started but deliberately did no work, because a cadence guard
    /// (e.g. <c>Discovery:MinRunInterval</c>) decided this iteration was too early.
    /// <para>
    /// Distinct from <see cref="Success"/> on purpose (#1149). A cadence guard asks
    /// "when did this process last actually run?", which it answers from
    /// <c>IProcessRunStore.GetLastCompletedRunStartAsync</c>. While a skip was recorded
    /// as <c>Success</c> it counted as its own last completed run, so every subsequent
    /// iteration re-read a completed run minutes old and skipped again — the skip
    /// re-armed itself and the process ran exactly once, ever. Skipped rows are excluded
    /// from that query, so the interval is always measured against real work.
    /// </para>
    /// <para>
    /// It is not a failure either: a skip is the guard working as designed, so read-side
    /// health treats it as a settled, healthy outcome — just one that did nothing.
    /// </para>
    /// </summary>
    Skipped = 4,

    /// <summary>
    /// The run was cut short by an orderly shutdown — the host asked it to stop and it
    /// did, at the first point its cancellation token reached. A redeploy is the ordinary
    /// cause (#1513).
    /// <para>
    /// Distinct from <see cref="Abandoned"/>, which is what a run looks like when nobody
    /// closed it: the two describe opposite things about the same interruption — one host
    /// stopped on request, the other died. Recording the difference is what lets the ops
    /// panels stay quiet about a deploy while still reporting a host that vanished.
    /// </para>
    /// <para>
    /// Not a failure: the work is simply unfinished and the next pass picks it up. It does
    /// not count as a completed run for a cadence guard either, for the same reason
    /// <see cref="Skipped"/> does not — nothing was actually done.
    /// </para>
    /// </summary>
    Cancelled = 5
}
