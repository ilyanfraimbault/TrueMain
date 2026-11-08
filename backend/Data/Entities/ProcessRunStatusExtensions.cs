namespace Data.Entities;

/// <summary>
/// The lifecycle of a <see cref="ProcessRunStatus"/> in one place (#2474): which statuses end a
/// run, which of those count against the process, which count as work done, and the only
/// transition a run takes. Every store write, query filter and health rule reads these instead
/// of restating its own list of statuses.
/// </summary>
public static class ProcessRunStatusExtensions
{
    /// <summary>The statuses that count as a failed run.</summary>
    public static readonly IReadOnlyList<ProcessRunStatus> FailureStatuses =
        [ProcessRunStatus.Failed, ProcessRunStatus.Abandoned];

    /// <summary>The statuses of a run that actually started its work.</summary>
    public static readonly IReadOnlyList<ProcessRunStatus> CompletedStatuses =
        [ProcessRunStatus.Success, ProcessRunStatus.Failed, ProcessRunStatus.Abandoned, ProcessRunStatus.Cancelled];

    /// <summary>Every status but <see cref="ProcessRunStatus.Running"/> ends a run for good.</summary>
    public static bool IsTerminal(this ProcessRunStatus status) => status != ProcessRunStatus.Running;

    /// <summary>
    /// A run that went wrong: it threw, or its owner died without closing it. Skipped (#1149) and
    /// Cancelled (#1513) are terminal but are not failures — a cadence guard declining to run and
    /// a redeploy stopping a run are both the system working as designed.
    /// </summary>
    public static bool CountsAsFailure(this ProcessRunStatus status)
        => status is ProcessRunStatus.Failed or ProcessRunStatus.Abandoned;

    /// <summary>
    /// A run that started spending its budget — what a cadence guard measures its interval from.
    /// Skipped is excluded because counting a skip makes it its own predecessor and the guard
    /// re-arms forever (#1149); Cancelled and the failures count because they did start work.
    /// </summary>
    public static bool CountsAsCompleted(this ProcessRunStatus status)
        => status.IsTerminal() && status != ProcessRunStatus.Skipped;

    /// <summary>
    /// The transition table: a run only ever moves from <see cref="ProcessRunStatus.Running"/> to
    /// one terminal status. A terminal run is never re-finalized — an Abandoned run turning into
    /// a Success would rewrite history the health alerting already acted on.
    /// </summary>
    public static bool CanTransitionTo(this ProcessRunStatus from, ProcessRunStatus to)
        => from == ProcessRunStatus.Running && to.IsTerminal();
}
