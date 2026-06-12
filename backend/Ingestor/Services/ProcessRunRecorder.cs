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

        var durationMs = (int)Math.Max(0, (finishedAtUtc - startedAtUtc).TotalMilliseconds);
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

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
