using Ingestor.Services;

namespace Ingestor.Processes;

/// <summary>
/// Decorator that wraps any <see cref="IIngestorProcess"/> with the
/// timing + try/catch + IProcessRunRecorder scaffolding that used to be
/// inlined into every *Process.
/// </summary>
/// <remarks>
/// A <c>Running</c> row is written before the inner process starts and then
/// flipped to <c>Success</c>/<c>Failed</c> on completion — so the row IS the
/// shared "what's running now" state. While the inner process runs, a background
/// loop refreshes the row's heartbeat so read queries can distinguish a genuinely
/// in-flight run from one whose host died (the latter ages out to Abandoned).
/// </remarks>
public sealed class RecordedProcess<TInner>(
    TInner inner,
    IProcessRunRecorder recorder,
    ILogger<RecordedProcess<TInner>> logger) : IIngestorProcess
    where TInner : IIngestorProcess
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    public string Name => inner.Name;

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
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
            var payload = await inner.RunCoreAsync(ct);
            await recorder.RecordSuccessAsync(runId, Name, startedAt, payload, ct);
            return payload;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown mid-run is NOT a failure, so don't record one — and
            // don't call RecordFailureAsync with the already-cancelled token (it
            // would just throw another OCE). The Running row is left as-is and
            // surfaces as Abandoned: via the stale-heartbeat read mapping within the
            // staleness window, then persisted by startup reconciliation on the next
            // boot. This keeps the Failed status meaningful (real errors only).
            throw;
        }
        catch (Exception ex)
        {
            await recorder.RecordFailureAsync(runId, Name, startedAt, ex, ct);
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

    private async Task RunHeartbeatLoopAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, ct);
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
