using Data.Entities;
using Data.Logging;
using Data.Ops.Mongo;
using Data.Pipeline;
using Ingestor.Processes.Summaries;
using Ingestor.Services;

namespace Ingestor.Processes;

/// <summary>
/// Decorator that wraps any <see cref="IIngestorProcess"/> with the
/// timing + try/catch + IProcessRunRecorder scaffolding that used to be
/// inlined into every *Process.
/// </summary>
/// <remarks>
/// A <c>Running</c> row is written before the inner process starts and then
/// flipped to <c>Success</c>/<c>Skipped</c>/<c>Failed</c> on completion, or to
/// <c>Cancelled</c> when a shutdown cuts the run short — so the
/// row IS the shared "what's running now" state. While the inner process runs, a
/// background loop refreshes the row's heartbeat so read queries can distinguish a
/// genuinely in-flight run from one whose host died (the latter ages out to Abandoned).
/// <para>
/// The status is the summary's own (<see cref="IProcessRunSummary.Status"/>); a step that is an
/// <see cref="ICadencedProcess"/> is not called at all while its interval has not elapsed, and a
/// <see cref="ProcessRunFailedException"/> is recorded Failed with the summary it carries.
/// </para>
/// </remarks>
public sealed class RecordedProcess<TInner>(
    TInner inner,
    PipelineProcess step,
    IProcessRunRecorder recorder,
    IProcessRunStore processRunStore,
    TimeProvider timeProvider,
    ILogger<RecordedProcess<TInner>> logger) : IIngestorProcess
    where TInner : IIngestorProcess
{
    /// <summary>
    /// How long the cancellation record may take before the shutdown moves on without it.
    /// Small on purpose: it is spent inside the container's stop grace period, alongside
    /// every other process still unwinding.
    /// </summary>
    private static readonly TimeSpan ShutdownRecordBudget = TimeSpan.FromSeconds(10);

    private string Name => step.Name;

    public async Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct)
    {
        var startedAt = timeProvider.GetUtcNow().UtcDateTime;
        var runId = await recorder.RecordStartAsync(Name, startedAt, ct);

        // Refresh the heartbeat on a background loop for as long as the inner
        // process runs. The linked source lets the finally below cancel the loop
        // the instant the run completes (success or fail) so it never outlives the
        // run, and the loop swallows everything so a heartbeat hiccup can't fail
        // an otherwise-healthy process.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatLoop = RunHeartbeatLoopAsync(runId, heartbeatCts.Token);
        try
        {
            var payload = await DeclinedByCadenceAsync(startedAt, ct) ?? await inner.RunCoreAsync(input, ct);
            var finishedAt = timeProvider.GetUtcNow().UtcDateTime;
            await recorder.RecordAsync(
                runId,
                Name,
                startedAt,
                finishedAt,
                payload?.Status ?? ProcessRunStatus.Success,
                payload,
                null,
                ct);
            // Ops event (#722): persisted by the Mongo sink despite being
            // Information, so every pipeline step's completion is visible and
            // filterable in the admin Logs panel alongside real diagnostics.
            logger.LogInformation(
                OpsEvents.ProcessRunCompleted,
                "Process {ProcessName} completed in {DurationMs} ms. Summary: {Summary}",
                Name,
                (long)(finishedAt - startedAt).TotalMilliseconds,
                payload);
            return payload;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown mid-run is NOT a failure, so don't record one — but it
            // is still an outcome, and one worth telling apart from a host that died
            // (#1513). Until this was recorded, the Running row was simply left behind:
            // it read as Abandoned for as long as the container stayed down and was only
            // settled by the next boot's reconciliation, so a redeploy and a crash left
            // the same trace. Written through a detached, time-boxed token because the
            // run's own is by now cancelled — RecordAsync with it would only throw a
            // second cancellation. Failed stays what it always was: real errors only.
            var cancelledAt = timeProvider.GetUtcNow().UtcDateTime;
            await RecordCancellationAsync(runId, startedAt, cancelledAt);
            throw;
        }
        catch (Exception ex)
        {
            var finishedAt = timeProvider.GetUtcNow().UtcDateTime;
            await recorder.RecordAsync(
                runId,
                Name,
                startedAt,
                finishedAt,
                ProcessRunStatus.Failed,
                (ex as ProcessRunFailedException)?.Summary,
                ex.Message,
                ct);
            logger.LogError(
                OpsEvents.ProcessRunFailed,
                ex,
                "Process {ProcessName} failed after {DurationMs} ms.",
                Name,
                (long)(finishedAt - startedAt).TotalMilliseconds);
            throw;
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            // The loop never throws (it catches everything internally), so this
            // await just joins it before the run returns.
            await heartbeatLoop;
        }
    }

    /// <summary>
    /// The skip to record instead of running a cadenced step whose last real run started less
    /// than its interval ago, or null to run it. The run being recorded is still Running, so
    /// the store's "last completed run" never answers with it.
    /// </summary>
    private async Task<IProcessRunSummary?> DeclinedByCadenceAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (inner is not ICadencedProcess { MinRunInterval: var interval } || interval <= TimeSpan.Zero)
        {
            return null;
        }

        var lastRunUtc = await processRunStore.GetLastCompletedRunStartAsync(Name, nowUtc, ct);
        if (lastRunUtc is null || nowUtc - lastRunUtc.Value >= interval)
        {
            return null;
        }

        logger.LogInformation(
            "{ProcessName} skipped: last run {LastRunUtc:o} is within MinRunInterval {Interval}.",
            Name,
            lastRunUtc,
            interval);
        return new SkippedSummary($"Within MinRunInterval; {step.Label.ToLowerInvariant()} skipped this iteration.");
    }

    private async Task RecordCancellationAsync(Guid runId, DateTime startedAt, DateTime cancelledAt)
    {
        using var shutdownCts = new CancellationTokenSource(ShutdownRecordBudget, timeProvider);

        try
        {
            await recorder.RecordAsync(
                runId,
                Name,
                startedAt,
                cancelledAt,
                ProcessRunStatus.Cancelled,
                null,
                "Cancelled: the host asked the run to stop (shutdown or redeploy).",
                shutdownCts.Token);
        }
        catch (Exception ex)
        {
            // Best-effort by design: the host is already stopping, and the worst case is
            // the behaviour this replaced — the row stays Running until the next boot
            // reconciles it to Abandoned. Holding the shutdown open for a store that is
            // not answering would be the worse trade.
            logger.LogWarning(
                ex,
                "Could not record the cancellation of {ProcessName}; the run will be reconciled at the next boot.",
                Name);
        }
    }

    private async Task RunHeartbeatLoopAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ProcessRunStaleness.HeartbeatInterval, timeProvider, ct);
                await recorder.HeartbeatAsync(runId, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the run completed and the finally cancelled the loop.
        }
        catch (Exception ex)
        {
            // A heartbeat is best-effort liveness; a failure must never bubble up
            // and fail the run. The worst case is a missed beat, which read
            // queries tolerate via the staleness threshold.
            logger.LogWarning(ex, "Heartbeat loop failed for run {RunId} ({ProcessName}).", runId, Name);
        }
    }
}
