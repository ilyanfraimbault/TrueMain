using Data.Entities;

namespace Ingestor.Services;

public interface IProcessRunRecorder
{
    /// <summary>
    /// Writes a <see cref="ProcessRunStatus.Running"/> row when a process starts
    /// and returns its id. The row is the shared "what's running now" state read
    /// by the ops API; it is later finalised by <see cref="RecordAsync"/>. If the
    /// host crashes before completion the row is simply left as stale-running.
    /// </summary>
    Task<Guid> RecordStartAsync(string processName, DateTime startedAtUtc, CancellationToken ct);

    /// <summary>
    /// Finalises the run identified by <paramref name="runId"/>, flipping its
    /// <see cref="ProcessRunStatus.Running"/> row to the terminal
    /// <paramref name="status"/> with the finish time, duration, summary and
    /// error. If the row can no longer be found (e.g. it was pruned) a fresh
    /// terminal row is inserted so the outcome is never lost.
    /// </summary>
    Task RecordAsync(
        Guid runId,
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        ProcessRunStatus status,
        object? summary,
        string? error,
        CancellationToken ct);

    /// <summary>
    /// Refreshes the liveness heartbeat of the in-flight run identified by
    /// <paramref name="runId"/>. No-op when the row is missing (pruned) or already
    /// terminal — only a <see cref="ProcessRunStatus.Running"/> row is touched.
    /// Called periodically by <c>RecordedProcess</c> so read queries can tell a
    /// genuinely-running row from an abandoned one.
    /// </summary>
    Task HeartbeatAsync(Guid runId, CancellationToken ct);

    /// <summary>
    /// Flips every row still in <see cref="ProcessRunStatus.Running"/> to
    /// <see cref="ProcessRunStatus.Abandoned"/> (stamping finish time, duration and
    /// an explanatory error) and returns how many were reconciled. Run once at
    /// ingestor startup: on the single-instance ingestor any row still running at
    /// boot was orphaned by the previous process and can never complete.
    /// </summary>
    Task<int> ReconcileOrphanedRunsAsync(CancellationToken ct);
}
