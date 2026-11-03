using Data.Metrics.Mongo;

namespace TrueMain.RequestLatency;

/// <summary>
/// Drains the <see cref="RequestLatencyAccumulator"/> into <c>request_latency_rollups</c>
/// every <see cref="FlushInterval"/>, and once more on shutdown (#2307).
/// </summary>
/// <remarks>
/// A failed write is dropped, counted on the accumulator and logged, never retried: the next
/// flush carries the next half-minute, and holding a failed batch would grow memory for as
/// long as Mongo stays down. The ops panel is telemetry, not a ledger.
/// </remarks>
public sealed partial class RequestLatencyFlusher(
    RequestLatencyAccumulator accumulator,
    IRequestLatencyStore store,
    ILogger<RequestLatencyFlusher> logger) : BackgroundService
{
    /// <summary>How often pending minutes are written.</summary>
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; StopAsync writes what is left.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Writes every pending rollup; returns how many were written.</summary>
    internal async Task<int> FlushAsync(CancellationToken ct)
    {
        var rollups = accumulator.Drain();
        if (rollups.Count == 0)
        {
            return 0;
        }

        try
        {
            return await store.WriteAsync(rollups, ct);
        }
        catch (Exception ex)
        {
            accumulator.CountDropped(rollups);
            if (ex is not OperationCanceledException)
            {
                LogFlushFailed(ex, rollups.Count);
            }

            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped {RollupCount} request-latency rollup(s): the write to Mongo failed")]
    private partial void LogFlushFailed(Exception exception, int rollupCount);
}
