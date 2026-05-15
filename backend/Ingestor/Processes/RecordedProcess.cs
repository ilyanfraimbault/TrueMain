using Ingestor.Services;

namespace Ingestor.Processes;

/// <summary>
/// Decorator that wraps any <see cref="IIngestorProcess"/> with the
/// timing + try/catch + IProcessRunRecorder scaffolding that used to be
/// inlined into every *Process.
/// </summary>
public sealed class RecordedProcess<TInner>(TInner inner, IProcessRunRecorder recorder) : IIngestorProcess
    where TInner : IIngestorProcess
{
    public string Name => inner.Name;

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            var payload = await inner.RunCoreAsync(ct);
            await recorder.RecordSuccessAsync(Name, startedAt, payload, ct);
            return payload;
        }
        catch (Exception ex)
        {
            await recorder.RecordFailureAsync(Name, startedAt, ex, ct);
            throw;
        }
    }
}
