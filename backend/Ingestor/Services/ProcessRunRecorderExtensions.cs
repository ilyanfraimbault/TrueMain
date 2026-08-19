using Data.Entities;
using Ingestor.Processes.Summaries;

namespace Ingestor.Services;

public static class ProcessRunRecorderExtensions
{
    /// <summary>
    /// Records a run that ended without throwing, as
    /// <see cref="ProcessRunStatus.Success"/> — or <see cref="ProcessRunStatus.Skipped"/>
    /// when the process reported that a cadence guard did the work of doing nothing.
    /// <para>
    /// The distinction is not cosmetic (#1149): a cadence guard measures its interval from
    /// the last <em>completed</em> run, so recording a skip as a success made the skip its
    /// own predecessor and re-armed it forever. Deciding the status here — from the summary
    /// the process already returns — keeps the rule in one place instead of asking every
    /// cadence-gated process to remember it.
    /// </para>
    /// </summary>
    public static Task RecordCompletionAsync(
        this IProcessRunRecorder runRecorder,
        Guid runId,
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        IProcessRunSummary? summary,
        CancellationToken ct)
    {
        var status = summary is SkippedSummary { Skipped: true }
            ? ProcessRunStatus.Skipped
            : ProcessRunStatus.Success;

        return runRecorder.RecordAsync(
            runId,
            processName,
            startedAtUtc,
            finishedAtUtc,
            status,
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
