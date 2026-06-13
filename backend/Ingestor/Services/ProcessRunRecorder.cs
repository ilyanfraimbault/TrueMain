using System.Text.Json;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Services;

public sealed class ProcessRunRecorder(
    IDataSessionFactory sessionFactory,
    IIterationContext iterationContext) : IProcessRunRecorder
{
    private const int MaxErrorLength = 2048;

    public async Task<Guid> RecordStartAsync(string processName, DateTime startedAtUtc, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);

        var run = new ProcessRun
        {
            ProcessName = processName,
            // Stamp the in-flight row with the iteration the Worker opened for this
            // pass (null when recorded outside a pass), so every run of the pass
            // groups under one iteration in the admin chain view.
            IterationId = iterationContext.CurrentIterationId,
            StartedAtUtc = startedAtUtc,
            // No finish yet; mirror StartedAtUtc as a placeholder (the column is
            // non-nullable) so the row reads as zero-duration until it completes.
            FinishedAtUtc = startedAtUtc,
            DurationMs = 0,
            Status = ProcessRunStatus.Running,
            // Seed the heartbeat at start so a run that dies before its first
            // refresh still ages out to Abandoned via the staleness threshold.
            LastHeartbeatAtUtc = startedAtUtc,
            Host = Environment.MachineName
        };

        session.ProcessRuns.Add(run);
        await session.SaveChangesAsync(ct);

        return run.Id;
    }

    public async Task RecordAsync(
        Guid runId,
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        ProcessRunStatus status,
        object? summary,
        string? error,
        CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);

        JsonDocument? summaryDoc = null;
        if (summary is not null)
        {
            summaryDoc = JsonDocument.Parse(JsonSerializer.Serialize(summary));
        }

        // Clamp before the int cast: an extreme span (e.g. a very stale run) could
        // exceed int.MaxValue ms (~24.8 days) and overflow into a negative duration.
        var durationMs = (int)Math.Clamp((finishedAtUtc - startedAtUtc).TotalMilliseconds, 0, int.MaxValue);
        var truncatedError = Truncate(error, MaxErrorLength);

        // Finalise the in-flight Running row in place. If the lookup misses (the
        // row was pruned by retention before completion, or runId isn't a real
        // id) fall back to inserting a fresh terminal row so the outcome is never
        // lost. FindAsync returns null for a non-existent key, so no Guid.Empty
        // special-case is needed.
        var existing = await session.ProcessRuns.GetByIdAsync(runId, ct);

        if (existing is null)
        {
            session.ProcessRuns.Add(new ProcessRun
            {
                ProcessName = processName,
                // The original Running row (which carried the iteration) is gone;
                // re-stamp from the still-current pass so the recovered terminal
                // row stays grouped with its iteration.
                IterationId = iterationContext.CurrentIterationId,
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = finishedAtUtc,
                DurationMs = durationMs,
                Status = status,
                Error = truncatedError,
                Host = Environment.MachineName,
                Summary = summaryDoc
            });
        }
        else
        {
            existing.FinishedAtUtc = finishedAtUtc;
            existing.DurationMs = durationMs;
            existing.Status = status;
            existing.Error = truncatedError;
            existing.Summary = summaryDoc;
        }

        await session.SaveChangesAsync(ct);
    }

    public async Task HeartbeatAsync(Guid runId, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);

        // Set-based UPDATE guarded on Status == Running: no read round-trip, and a
        // no-op when the row is gone (pruned) or already terminal — only an
        // in-flight Running row carries a meaningful heartbeat, and refreshing a
        // finished row would resurrect it as "fresh".
        await session.ProcessRuns.TouchHeartbeatAsync(runId, DateTime.UtcNow, ct);
    }

    public async Task<int> ReconcileOrphanedRunsAsync(CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);

        // Single-instance ingestor: anything still Running at startup was owned by
        // the previous process that is now gone, so it can never complete. Flip
        // every such row to Abandoned with a real finish time and duration so it
        // stops reading as perpetually in-flight.
        var orphaned = await session.ProcessRuns.GetRunningAsync(ct);
        if (orphaned.Count == 0)
        {
            return 0;
        }

        var finishedAtUtc = DateTime.UtcNow;
        foreach (var run in orphaned)
        {
            run.FinishedAtUtc = finishedAtUtc;
            // Clamp before the int cast: an orphaned run can be arbitrarily old, and
            // a span over int.MaxValue ms (~24.8 days) would overflow to negative.
            run.DurationMs = (int)Math.Clamp((finishedAtUtc - run.StartedAtUtc).TotalMilliseconds, 0, int.MaxValue);
            run.Status = ProcessRunStatus.Abandoned;
            run.Error = "Abandoned: ingestor restarted while this run was in flight.";
        }

        await session.SaveChangesAsync(ct);
        return orphaned.Count;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
