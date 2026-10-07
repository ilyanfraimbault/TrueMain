using System.Diagnostics.Metrics;
using Data.Metrics.Mongo;

namespace Ingestor.Services;

/// <summary>
/// Makes the <see cref="IngestorMetrics"/> instruments visible (#1636): a
/// <see cref="MeterListener"/> on this host's <c>TrueMain.Ingestor</c> meter folds every
/// measurement into per-minute rollups, flushed to Mongo once a minute and read by the admin
/// Riot API tab. The meter was declared with no exporter wired, so until this nothing ever
/// collected it.
/// </summary>
/// <remarks>
/// <para>
/// Mongo rather than OpenTelemetry (product decision on #1636): the admin portal already reads
/// its observability data from Mongo, and a Prometheus or OTLP sink would need a collector and
/// containers the VPS has no room for.
/// </para>
/// <para>
/// Only the meter this host's <see cref="IMeterFactory"/> created is listened to — the
/// listener is process-global, and a meter of the same name from another factory (a test's)
/// must not leak into this host's rollups. Registered ahead of the <c>Worker</c>: hosted
/// services stop in reverse order, so the final flush runs after the last pass recorded.
/// </para>
/// </remarks>
public sealed partial class IngestorMeterExporter : BackgroundService
{
    /// <summary>How often the folded rollups are written — once per rollup minute.</summary>
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the shutdown flush may take. Well under the host's 100 s shutdown budget: a hung
    /// Mongo must cost the last minute of telemetry, not hold the container until it is killed.
    /// </summary>
    internal static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    private readonly IMeterRollupStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestorMeterExporter> _logger;
    private readonly MeterRollupAccumulator _accumulator;
    private readonly MeterListener _listener;

    public IngestorMeterExporter(
        IMeterFactory meterFactory,
        IMeterRollupStore store,
        TimeProvider timeProvider,
        ILogger<IngestorMeterExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
        _accumulator = new MeterRollupAccumulator(timeProvider);
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == IngestorMetrics.MeterName
                    && ReferenceEquals(instrument.Meter.Scope, meterFactory))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => _accumulator.Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => _accumulator.Record(instrument, value, tags));
        // Started here, not in ExecuteAsync: IngestorMetrics may record before the hosted
        // service runs, and Start() also enables the instruments created before it.
        _listener.Start();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host stopping: StopAsync writes what the last minute folded.
        }
    }

    /// <summary>
    /// Stops the periodic loop, then writes what it had not flushed yet. Done here rather than
    /// at the end of <see cref="ExecuteAsync"/>, which does not run at all when the host stops
    /// before it was scheduled; bounded by <see cref="FinalFlushTimeout"/> whatever the host's
    /// own token allows.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        using var budget = new CancellationTokenSource(FinalFlushTimeout, _timeProvider);
        using var finalFlush = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        await FlushAsync(finalFlush.Token);
    }

    /// <summary>Writes everything folded since the previous flush; a failure drops it, logged.</summary>
    internal async Task FlushAsync(CancellationToken ct)
    {
        var rollups = _accumulator.Drain();
        if (rollups.Count == 0)
        {
            return;
        }

        try
        {
            await _store.WriteAsync(rollups, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown raced a periodic flush, or the final flush ran out of time: either way
            // the drained minute is gone, and saying so is cheaper than holding the host.
            LogFlushCancelled(_logger, rollups.Count);
        }
        catch (Exception ex)
        {
            LogFlushFailed(_logger, ex, rollups.Count);
        }
    }

    public override void Dispose()
    {
        _listener.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Count} meter rollup(s); dropping them.")]
    private static partial void LogFlushFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Meter rollup flush cancelled; dropping {Count} rollup(s).")]
    private static partial void LogFlushCancelled(ILogger logger, int count);
}
