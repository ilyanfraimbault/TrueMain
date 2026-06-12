using System.Text.Json;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Services;

public sealed class ProcessRunRecorder(IDataSessionFactory sessionFactory) : IProcessRunRecorder
{
    private const int MaxErrorLength = 2048;

    public async Task<Guid> RecordStartAsync(string processName, DateTime startedAtUtc, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);

        var run = new ProcessRun
        {
            ProcessName = processName,
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

        // Finalise the in-flight Running row in place. If it has vanished (e.g.
        // pruned by retention before completion) fall back to inserting a fresh
        // terminal row so the outcome is never lost.
        var existing = runId == Guid.Empty
            ? null
            : await session.ProcessRuns.GetByIdAsync(runId, ct);

        if (existing is null)
        {
            session.ProcessRuns.Add(new ProcessRun
            {
                ProcessName = processName,
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
