using Data.Entities;

namespace Ingestor.Services;

public static class ProcessRunRecorderExtensions
{
    public static Task RecordSuccessAsync(
        this IProcessRunRecorder runRecorder,
        string processName,
        DateTime startedAtUtc,
        object? summary,
        CancellationToken ct)
    {
        return runRecorder.RecordAsync(
            processName,
            startedAtUtc,
            DateTime.UtcNow,
            ProcessRunStatus.Success,
            summary,
            null,
            ct);
    }

    public static Task RecordFailureAsync(
        this IProcessRunRecorder runRecorder,
        string processName,
        DateTime startedAtUtc,
        Exception exception,
        CancellationToken ct)
    {
        return runRecorder.RecordAsync(
            processName,
            startedAtUtc,
            DateTime.UtcNow,
            ProcessRunStatus.Failed,
            null,
            exception.Message,
            ct);
    }

    public static Task RecordNoOpAsync(
        this IProcessRunRecorder runRecorder,
        string processName,
        DateTime startedAtUtc,
        object summary,
        CancellationToken ct)
    {
        return runRecorder.RecordSuccessAsync(processName, startedAtUtc, summary, ct);
    }
}
