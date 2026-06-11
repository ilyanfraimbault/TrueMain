using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>
/// Background service that drains the <see cref="MongoLogChannel"/> and
/// batch-inserts records as <see cref="MongoLogDocument"/>s into the diagnostic
/// <c>logs</c> collection. Batches by count
/// (<see cref="MongoLoggingOptions.BatchSize"/>) or by time
/// (<see cref="MongoLoggingOptions.FlushInterval"/>), whichever comes first, so a
/// single error is still persisted promptly. Replaces the Postgres
/// <c>DatabaseLogSink</c> (#416) and keeps its bounded channel, batch insert,
/// graceful degradation and re-entry guard.
/// </summary>
/// <remarks>
/// This service deliberately never logs through <c>ILogger</c>: doing so could
/// re-enter the channel it is draining. All failures are swallowed and reported
/// to <see cref="Console.Error"/> instead, so a Mongo outage degrades to "logs
/// not persisted" rather than crashing the host or looping.
/// </remarks>
internal sealed class MongoLogSink(
    MongoLogChannel channel,
    MongoLogContext context,
    IOptions<MongoLoggingOptions> options) : BackgroundService
{
    private static readonly InsertManyOptions UnorderedInsert = new() { IsOrdered = false };
    private readonly MongoLoggingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // context.IsActive already implies _options.IsActive (the context only
        // builds its database when logging is active), so this single guard covers
        // both the disabled-config and no-connection-string cases.
        if (!context.IsActive)
        {
            return;
        }

        var batchSize = Math.Max(1, _options.BatchSize);
        var reader = channel.Reader;

        // Create the TTL + read indexes once on startup. A failure here must not
        // crash the host; logs just won't have their indexes until a later boot.
        // Cancellation (a fast shutdown racing index creation) must NOT skip the
        // drain below, or records already queued would be lost — so it falls
        // through to the best-effort final drain rather than returning.
        try
        {
            await context.EnsureIndexesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await DrainRemainingAsync(reader, batchSize);
            return;
        }
        catch (Exception ex)
        {
            ReportError($"[MongoLogSink] failed to ensure indexes: {ex}");
        }

        var flushInterval = _options.FlushInterval > TimeSpan.Zero
            ? _options.FlushInterval
            : TimeSpan.FromSeconds(2);
        var buffer = new List<MongoLogDocument>(batchSize);

        try
        {
            // WaitToReadAsync completes when an item is available or the channel
            // is completed. The outer loop blocks (no busy-wait) until there is
            // something to do.
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                // Accumulate up to a full batch, but never wait longer than the
                // flush interval so a lone error is not stranded in the buffer.
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
        // Stop accepting new records so the drain terminates deterministically,
        // then let the base implementation await ExecuteAsync's completion.
        channel.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task DrainRemainingAsync(ChannelReader<MongoLogRecord> reader, int batchSize)
    {
        // Shutdown path: persist anything still queued with a fresh, un-cancelled
        // token so the final flush is not aborted by the stopping token.
        var buffer = new List<MongoLogDocument>(batchSize);
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

    private async Task PersistAsync(IReadOnlyList<MongoLogDocument> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
        {
            return;
        }

        try
        {
            // Unordered insert: one malformed/oversized document must not abort the
            // rest of the batch.
            await context.Logs.InsertManyAsync(documents, UnorderedInsert, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown raced the flush; drop this batch rather than blocking exit.
        }
        catch (Exception ex)
        {
            // A persistence failure (Mongo down, network blip, etc.) must not crash
            // the host or feed back into the logging pipeline. Report and drop.
            ReportError($"[MongoLogSink] failed to persist {documents.Count} log record(s): {ex}");
        }
    }

    private static MongoLogDocument ToDocument(MongoLogRecord record) => new()
    {
        TimestampUtc = record.TimestampUtc,
        Level = record.Level.ToString(),
        // Truncate the bounded infra fields exactly as the old Postgres
        // DatabaseLogSink did (Category->256, ProcessName->64, Host->128) so a
        // runaway category/host name can't bloat a document. Message/Exception are
        // intentionally left uncapped — a stack trace must be persisted in full.
        Category = TruncateRequired(record.Category, 256),
        Message = record.Message,
        Exception = record.Exception,
        ProcessName = Truncate(record.ProcessName, 64),
        Host = Truncate(record.Host, 128),
        EventId = record.EventId == 0 ? null : record.EventId
    };

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static string TruncateRequired(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

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
