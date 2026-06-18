using Data.Entities;

namespace Ingestor.Services;

public static class ProcessRunRecorderExtensions
{
    public static Task RecordSuccessAsync(
        this IProcessRunRecorder runRecorder,
        Guid runId,
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        object? summary,
        CancellationToken ct)
    {
        return runRecorder.RecordAsync(
            runId,
            processName,
            startedAtUtc,
            finishedAtUtc,
            ProcessRunStatus.Success,
            summary,
            null,
            ct);
    }

    public static Task RecordFailureAsync(
        this IProcessRunRecorder runRecorder,
        Guid runId,
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        Exception exception,
        CancellationToken ct)
    {
        return runRecorder.RecordAsync(
            runId,
            processName,
            startedAtUtc,
            finishedAtUtc,
            ProcessRunStatus.Failed,
            null,
            exception.Message,
            ct);
    }
}
