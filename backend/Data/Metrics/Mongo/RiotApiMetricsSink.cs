using System.Threading.Channels;
using Data.Logging.Mongo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Background service that drains the <see cref="RiotApiMetricsChannel"/> and
/// batch-inserts records as <see cref="RiotApiCallDocument"/>s into the
/// <c>riot_api_calls</c> collection. Batches by count
/// (<see cref="MongoLoggingOptions.BatchSize"/>) or time
/// (<see cref="MongoLoggingOptions.FlushInterval"/>), whichever comes first.
/// A trimmed-down sibling of <c>MongoLogSink</c> sharing its bounded-channel,
/// batch-insert, graceful-degradation and re-entry-safe shape.
/// </summary>
/// <remarks>
/// Like the log sink, this never logs through <c>ILogger</c> on its failure path
/// (it could re-enter the logging Mongo client); failures are reported to
/// <see cref="Console.Error"/> and dropped, so a Mongo outage degrades to "no
/// metrics persisted" rather than crashing the host.
/// </remarks>
internal sealed class RiotApiMetricsSink(
    RiotApiMetricsChannel channel,
    MongoLogContext context,
    IOptions<MongoLoggingOptions> options) : BackgroundService
{
    private static readonly InsertManyOptions UnorderedInsert = new() { IsOrdered = false };
    private readonly MongoLoggingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // context.IsActive already implies the Mongo store is configured, so this
        // single guard covers the disabled-config and no-connection-string cases.
        if (!context.IsActive)
        {
            return;
        }

        var batchSize = Math.Max(1, _options.BatchSize);
        var reader = channel.Reader;

        // Create the TTL + read indexes once on startup. A failure here must not
        // crash the host; cancellation must still fall through to the final drain
        // so queued records aren't lost.
        try
        {
            await context.EnsureRiotApiCallIndexesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await DrainRemainingAsync(reader, batchSize);
            return;
        }
        catch (Exception ex)
        {
            ReportError($"[RiotApiMetricsSink] failed to ensure indexes: {ex}");
        }

        var flushInterval = _options.FlushInterval > TimeSpan.Zero
            ? _options.FlushInterval
            : TimeSpan.FromSeconds(2);
        var buffer = new List<RiotApiCallDocument>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                using var batchWindow = new CancellationTokenSource(flushInterval);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, batchWindow.Token);

                buffer.Clear();
                buffer.Add(ToDocument(await reader.ReadAsync(stoppingToken)));

                try
                {
                    while (buffer.Count < batchSize
                           && await reader.WaitToReadAsync(linked.Token)
                           && reader.TryRead(out var next))
                    {
                        buffer.Add(ToDocument(next));
                    }
                }
                catch (OperationCanceledException) when (batchWindow.IsCancellationRequested
                                                         && !stoppingToken.IsCancellationRequested)
                {
                    // Flush window elapsed; persist whatever we have so far.
                }

                await PersistAsync(buffer, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down; fall through to a best-effort final drain.
        }

        await DrainRemainingAsync(reader, batchSize);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new records so the drain terminates deterministically.
        channel.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task DrainRemainingAsync(ChannelReader<RiotApiCallRecord> reader, int batchSize)
    {
        var buffer = new List<RiotApiCallDocument>(batchSize);
        while (reader.TryRead(out var record))
        {
            buffer.Add(ToDocument(record));
            if (buffer.Count >= batchSize)
            {
                await PersistAsync(buffer, CancellationToken.None);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await PersistAsync(buffer, CancellationToken.None);
        }
    }

    private async Task PersistAsync(IReadOnlyList<RiotApiCallDocument> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
        {
            return;
        }

        try
        {
            await context.RiotApiCalls.InsertManyAsync(documents, UnorderedInsert, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown raced the flush; drop this batch rather than blocking exit.
        }
        catch (Exception ex)
        {
            ReportError($"[RiotApiMetricsSink] failed to persist {documents.Count} record(s): {ex}");
        }
    }

    private RiotApiCallDocument ToDocument(RiotApiCallRecord record) => new()
    {
        TimestampUtc = record.TimestampUtc,
        Endpoint = Truncate(record.Endpoint, 128) ?? string.Empty,
        Method = Truncate(record.Method, 16) ?? string.Empty,
        StatusCode = record.StatusCode,
        LatencyMs = record.LatencyMs,
        Route = Truncate(record.Route, 32),
        AppRateLimit = Truncate(record.AppRateLimit, 128),
        AppRateLimitCount = Truncate(record.AppRateLimitCount, 128),
        MethodRateLimit = Truncate(record.MethodRateLimit, 128),
        MethodRateLimitCount = Truncate(record.MethodRateLimitCount, 128),
        RetryAfterSeconds = record.RetryAfterSeconds,
        RateLimitType = Truncate(record.RateLimitType, 32),
        // Host-level tag, stamped here from config rather than carried per record.
        ProcessName = Truncate(_options.ProcessName, 64)
    };

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static void ReportError(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Nothing safe left to do.
        }
    }
}
