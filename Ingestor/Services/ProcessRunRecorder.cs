using System.Text.Json;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Services;

public sealed class ProcessRunRecorder(IDataSessionFactory sessionFactory) : IProcessRunRecorder
{
    private const int MaxErrorLength = 2048;

    public async Task RecordAsync(
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

        session.ProcessRuns.Add(new ProcessRun
        {
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            DurationMs = (int)Math.Max(0, (finishedAtUtc - startedAtUtc).TotalMilliseconds),
            Status = status,
            Error = Truncate(error, MaxErrorLength),
            Host = Environment.MachineName,
            Summary = summaryDoc
        });

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
