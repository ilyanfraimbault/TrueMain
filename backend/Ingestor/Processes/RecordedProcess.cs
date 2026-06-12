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
/// shared "what's running now" state. On a host crash the row is left as
/// stale-running rather than over-engineered into a heartbeat.
/// </remarks>
public sealed class RecordedProcess<TInner>(TInner inner, IProcessRunRecorder recorder) : IIngestorProcess
    where TInner : IIngestorProcess
{
    public string Name => inner.Name;

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var runId = await recorder.RecordStartAsync(Name, startedAt, ct);
        try
        {
            var payload = await inner.RunCoreAsync(ct);
            await recorder.RecordSuccessAsync(runId, Name, startedAt, payload, ct);
            return payload;
        }
        catch (Exception ex)
        {
            await recorder.RecordFailureAsync(runId, Name, startedAt, ex, ct);
            throw;
        }
    }
}
