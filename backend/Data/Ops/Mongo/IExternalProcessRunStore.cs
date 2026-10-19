using Data.Entities;

namespace Data.Ops.Mongo;

/// <summary>
/// The writes an ops script makes to its own <c>process_runs</c> document (#2119): the
/// out-of-repo scrapers start a run through <see cref="IProcessRunStore.InsertAsync"/>, then
/// keep it alive and close it through this store. Split from <see cref="IProcessRunStore"/>
/// because those runs carry a summary that changes while they work, and because only the
/// Ingestor's recorder may finalise an Ingestor run.
/// </summary>
public interface IExternalProcessRunStore
{
    /// <summary>The run with this id, or null (unknown id, or Mongo not configured).</summary>
    Task<ProcessRunDocument?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Refreshes the heartbeat of a Running run and, when <paramref name="summaryJson"/> is
    /// not null, replaces its summary. Returns false when no Running document matched: a
    /// finished run is never resurrected.
    /// </summary>
    Task<bool> HeartbeatAsync(Guid id, DateTime nowUtc, string? summaryJson, CancellationToken ct);

    /// <summary>
    /// Closes a Running run with its terminal status. Returns false when no Running document
    /// matched, so a run is finished once and a late retry cannot rewrite its outcome.
    /// </summary>
    Task<bool> FinishAsync(
        Guid id,
        DateTime finishedAtUtc,
        int durationMs,
        ProcessRunStatus status,
        string? error,
        string? summaryJson,
        CancellationToken ct);
}
