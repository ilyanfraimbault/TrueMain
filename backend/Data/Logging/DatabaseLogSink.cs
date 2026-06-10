using Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Data.Logging;

/// <summary>
/// Background service that drains the <see cref="DatabaseLogChannel"/> and
/// batch-inserts records as <see cref="LogEntry"/> rows using a
/// <see cref="TrueMainDbContext"/> resolved from a fresh DI scope (this sink is a
/// singleton <see cref="IHostedService"/> and cannot capture the scoped context
/// factory directly). Batches by count (<see cref="LoggingSinkOptions.BatchSize"/>)
/// or by time (<see cref="LoggingSinkOptions.FlushInterval"/>), whichever comes
/// first, so a single error is still persisted promptly.
/// </summary>
/// <remarks>
/// This service deliberately never logs through <c>ILogger</c>: doing so could
/// re-enter the channel it is draining. All failures are swallowed and reported
/// to <see cref="Console.Error"/> instead, so a database outage degrades to
/// "logs not persisted" rather than crashing the host or looping.
/// </remarks>
internal sealed class DatabaseLogSink(
    DatabaseLogChannel channel,
    IServiceScopeFactory scopeFactory,
    IOptions<LoggingSinkOptions> options) : BackgroundService
{
    private readonly LoggingSinkOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var batchSize = Math.Max(1, _options.BatchSize);
        var flushInterval = _options.FlushInterval > TimeSpan.Zero
            ? _options.FlushInterval
            : TimeSpan.FromSeconds(2);
        var reader = channel.Reader;
        var buffer = new List<LogRecord>(batchSize);

        try
        {
            // WaitToReadAsync completes when an item is available or the channel
            // is completed; ReadAllAsync below would also end on completion. The
            // outer loop blocks (no busy-wait) until there is something to do.
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                // Accumulate up to a full batch, but never wait longer than the
                // flush interval so a lone error is not stranded in the buffer.
                using var batchWindow = new CancellationTokenSource(flushInterval);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, batchWindow.Token);

                buffer.Clear();
                buffer.Add(await reader.ReadAsync(stoppingToken));

                try
                {
                    while (buffer.Count < batchSize
                           && await reader.WaitToReadAsync(linked.Token)
                           && reader.TryRead(out var next))
                    {
                        buffer.Add(next);
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

    private async Task DrainRemainingAsync(
        System.Threading.Channels.ChannelReader<LogRecord> reader,
        int batchSize)
    {
        // Shutdown path: persist anything still queued with a fresh, un-cancelled
        // token so the final flush is not aborted by the stopping token.
        var buffer = new List<LogRecord>(batchSize);
        while (reader.TryRead(out var record))
        {
            buffer.Add(record);
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

    private async Task PersistAsync(IReadOnlyList<LogRecord> records, CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return;
        }

        try
        {
            // Resolve the scoped DbContext from a fresh scope: this sink is a
            // singleton IHostedService and cannot capture the scoped context
            // factory directly (DI scope validation rejects that at startup).
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TrueMainDbContext>();
            foreach (var record in records)
            {
                db.LogEntries.Add(new LogEntry
                {
                    TimestampUtc = record.TimestampUtc,
                    Level = record.Level.ToString(),
                    Category = TruncateRequired(record.Category, 256),
                    Message = record.Message,
                    Exception = record.Exception,
                    ProcessName = Truncate(record.ProcessName, 64),
                    Host = Truncate(record.Host, 128),
                    EventId = record.EventId == 0 ? null : record.EventId
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown raced the flush; drop this batch rather than blocking exit.
        }
        catch (Exception ex)
        {
            // A persistence failure (DB down, schema drift, etc.) must not crash
            // the host or feed back into the logging pipeline. Report and drop.
            try
            {
                Console.Error.WriteLine(
                    $"[DatabaseLogSink] failed to persist {records.Count} log record(s): {ex}");
            }
            catch
            {
                // Nothing safe left to do.
            }
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string TruncateRequired(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
