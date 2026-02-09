using Data.Entities;

namespace Ingestor.Services;

public interface IProcessRunRecorder
{
    Task RecordAsync(
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        ProcessRunStatus status,
        object? summary,
        string? error,
        CancellationToken ct);
}
