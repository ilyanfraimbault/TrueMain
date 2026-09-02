using Data.Entities;
using Data.Ops.Mongo;
using Ingestor.Processes.Summaries;

namespace Ingestor.Services;

public sealed class ProcessRunRecorder(
    IProcessRunStore store,
    IIterationContext iterationContext) : IProcessRunRecorder
{
    private const int MaxErrorLength = 2048;

    public async Task<Guid> RecordStartAsync(string processName, DateTime startedAtUtc, CancellationToken ct)
    {
        var run = new ProcessRunDocument
        {
            Id = Guid.NewGuid(),
            ProcessName = processName,
            // Stamp the in-flight document with the iteration the Worker opened for
            // this pass (null when recorded outside a pass), so every run of the
            // pass groups under one iteration in the admin chain view.
            IterationId = iterationContext.CurrentIterationId,
            // The lane this pass covers (#1362). Recorded per run rather than derived
            // from the set of processes that happen to have run: a pass that has only
            // reached its first step is indistinguishable from a one-off single-process
            // run by its contents alone.
            JobMode = iterationContext.CurrentJobMode?.ToString(),
            StartedAtUtc = startedAtUtc,
            // No finish yet; mirror StartedAtUtc as a placeholder so the document
            // reads as zero-duration until it completes.
            FinishedAtUtc = startedAtUtc,
            DurationMs = 0,
            Status = ProcessRunStatus.Running,
            // Seed the heartbeat at start so a run that dies before its first
            // refresh still ages out to Abandoned via the staleness threshold.
            LastHeartbeatAtUtc = startedAtUtc,
            Host = Environment.MachineName
        };

        await store.InsertAsync(run, ct);

        return run.Id;
    }

    public async Task RecordAsync(
        Guid runId,
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        ProcessRunStatus status,
        IProcessRunSummary? summary,
        string? error,
        CancellationToken ct)
    {
        // Source-generated metadata (#268), persisted as the raw JSON text.
        var summaryJson = summary is null ? null : ProcessRunSummaryJson.Serialize(summary);

        // Clamp before the int cast: an extreme span (e.g. a very stale run) could
        // exceed int.MaxValue ms (~24.8 days) and overflow into a negative duration.
        var durationMs = (int)Math.Clamp((finishedAtUtc - startedAtUtc).TotalMilliseconds, 0, int.MaxValue);
        var truncatedError = Truncate(error, MaxErrorLength);

        // Finalise the in-flight Running document in place. If the update misses
        // (the document was reaped by the TTL before completion, or runId isn't a
        // real id) fall back to inserting a fresh terminal document so the outcome
        // is never lost.
        var finalized = await store.FinalizeAsync(
            runId, finishedAtUtc, durationMs, status, truncatedError, summaryJson, ct);

        if (!finalized)
        {
            await store.InsertAsync(
                new ProcessRunDocument
                {
                    Id = runId == Guid.Empty ? Guid.NewGuid() : runId,
                    ProcessName = processName,
                    // The original Running document (which carried the iteration) is
                    // gone; re-stamp from the still-current pass so the recovered
                    // terminal document stays grouped with its iteration.
                    IterationId = iterationContext.CurrentIterationId,
                    StartedAtUtc = startedAtUtc,
                    FinishedAtUtc = finishedAtUtc,
                    DurationMs = durationMs,
                    Status = status,
                    Error = truncatedError,
                    Host = Environment.MachineName,
                    SummaryJson = summaryJson
                },
                ct);
        }
    }

    public Task HeartbeatAsync(Guid runId, CancellationToken ct)
        // Guarded on Status == Running inside the store: a no-op when the document
        // is gone (reaped) or already terminal — only an in-flight Running document
        // carries a meaningful heartbeat, and refreshing a finished one would
        // resurrect it as "fresh".
        => store.TouchHeartbeatAsync(runId, DateTime.UtcNow, ct);

    public Task<int> ReconcileOrphanedRunsAsync(IReadOnlyCollection<string> processNames, CancellationToken ct)
        // Anything still Running at startup among *this* instance's own processes was owned
        // by the previous incarnation of it, which is gone, so it can never complete. The
        // scoping matters since #1362: with a fetch lane and an aggregate lane in separate
        // processes, an unscoped sweep would abandon runs that are very much alive.
        => store.AbandonRunningAsync(
            DateTime.UtcNow,
            "Abandoned: ingestor restarted while this run was in flight.",
            processNames,
            ct);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
