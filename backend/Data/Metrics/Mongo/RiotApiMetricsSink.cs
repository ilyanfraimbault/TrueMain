using System.Linq.Expressions;
using System.Threading.Channels;
using Data.Logging.Mongo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Background service that drains the <see cref="RiotApiMetricsChannel"/> and folds
/// each batch into per-minute <see cref="RiotApiCallRollupDocument"/> rollups in the
/// <c>riot_api_call_rollups</c> collection, one <c>$inc</c>-upsert per
/// <c>(minute, endpoint, statusCode)</c> key. Batches by count
/// (<see cref="MongoLoggingOptions.BatchSize"/>) or time
/// (<see cref="MongoLoggingOptions.FlushInterval"/>), whichever comes first.
/// A trimmed-down sibling of <c>MongoLogSink</c> sharing its bounded-channel,
/// graceful-degradation and re-entry-safe shape — but rolling up instead of
/// inserting one document per call, so the collection (and the panel's
/// whole-window aggregations) no longer scale with raw call volume.
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
    // Unordered: one failed upsert (e.g. a transient duplicate-key race on insert)
    // must not abort the rest of the batch.
    private static readonly BulkWriteOptions UnorderedBulk = new() { IsOrdered = false };
    private static readonly UpdateDefinitionBuilder<RiotApiCallRollupDocument> Update =
        Builders<RiotApiCallRollupDocument>.Update;
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
        var buffer = new List<RiotApiCallRecord>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
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
        // Stop accepting new records so the drain terminates deterministically.
        channel.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task DrainRemainingAsync(ChannelReader<RiotApiCallRecord> reader, int batchSize)
    {
        var buffer = new List<RiotApiCallRecord>(batchSize);
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

    /// <summary>
    /// Folds the drained <paramref name="records"/> into per-minute rollups and
    /// applies them as one unordered <c>$inc</c>-upsert per
    /// <c>(bucketStartUtc, endpoint, statusCode)</c> key. The unique index on that
    /// triple (see <c>MongoLogContext.EnsureRiotApiCallIndexesAsync</c>) makes each
    /// upsert target exactly one document; the filter supplies the key fields on
    /// insert, so they are not re-set in the update.
    /// </summary>
    private async Task PersistAsync(IReadOnlyList<RiotApiCallRecord> records, CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return;
        }

        var rollups = Fold(records);
        if (rollups.Count == 0)
        {
            return;
        }

        try
        {
            await context.RiotApiCallRollups.BulkWriteAsync(rollups, UnorderedBulk, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown raced the flush; drop this batch rather than blocking exit.
        }
        catch (Exception ex)
        {
            ReportError($"[RiotApiMetricsSink] failed to persist {rollups.Count} rollup(s): {ex}");
        }
    }

    /// <summary>
    /// Groups a batch by <c>(minute, endpoint, statusCode)</c> and builds one upsert
    /// per group: <c>$inc</c> the count and latency sum, <c>$max</c> the last-called
    /// timestamp, and <c>$set</c> the last-seen descriptive/rate-limit fields.
    /// </summary>
    /// <remarks>
    /// Only <em>non-null</em> field values are <c>$set</c>: a later call in the same
    /// minute whose Riot response omitted a header (e.g. a 429 with no
    /// <c>Retry-After</c>) must not erase a value an earlier call stored — a
    /// <c>$set: null</c> would overwrite it unconditionally. The accumulator keeps
    /// the last-seen non-null value per field; because the channel drains in FIFO
    /// (roughly timestamp) order, that is the freshest value in the bucket.
    /// </remarks>
    private List<WriteModel<RiotApiCallRollupDocument>> Fold(IReadOnlyList<RiotApiCallRecord> records)
    {
        var accumulators = new Dictionary<(DateTime Bucket, string Endpoint, int StatusCode), Accumulator>();

        foreach (var record in records)
        {
            var bucket = new DateTime(
                record.TimestampUtc.Year, record.TimestampUtc.Month, record.TimestampUtc.Day,
                record.TimestampUtc.Hour, record.TimestampUtc.Minute, 0, DateTimeKind.Utc);
            var endpoint = Truncate(record.Endpoint, 128) ?? string.Empty;
            var key = (bucket, endpoint, record.StatusCode);

            if (!accumulators.TryGetValue(key, out var acc))
            {
                acc = new Accumulator();
                accumulators[key] = acc;
            }

            acc.Add(record);
        }

        var writes = new List<WriteModel<RiotApiCallRollupDocument>>(accumulators.Count);
        foreach (var ((bucket, endpoint, statusCode), acc) in accumulators)
        {
            var filter = Builders<RiotApiCallRollupDocument>.Filter.And(
                Builders<RiotApiCallRollupDocument>.Filter.Eq(doc => doc.BucketStartUtc, bucket),
                Builders<RiotApiCallRollupDocument>.Filter.Eq(doc => doc.Endpoint, endpoint),
                Builders<RiotApiCallRollupDocument>.Filter.Eq(doc => doc.StatusCode, statusCode));

            // Key fields (bucket/endpoint/statusCode) come from the filter on insert,
            // so they are intentionally not in the update — setting them too would
            // conflict with the filter-implied values.
            var updates = new List<UpdateDefinition<RiotApiCallRollupDocument>>
            {
                Update.Inc(doc => doc.Count, acc.Count),
                Update.Inc(doc => doc.SumLatencyMs, acc.SumLatencyMs),
                Update.Max(doc => doc.LastCalledAtUtc, acc.LastCalledAtUtc)
            };

            // Last-seen non-null wins; a null is skipped so it never clobbers a
            // value an earlier flush persisted for the same bucket.
            SetIfPresent(updates, doc => doc.Method, Truncate(acc.Method, 16));
            SetIfPresent(updates, doc => doc.Route, Truncate(acc.Route, 32));
            SetIfPresent(updates, doc => doc.AppRateLimit, Truncate(acc.AppRateLimit, 128));
            SetIfPresent(updates, doc => doc.AppRateLimitCount, Truncate(acc.AppRateLimitCount, 128));
            SetIfPresent(updates, doc => doc.MethodRateLimit, Truncate(acc.MethodRateLimit, 128));
            SetIfPresent(updates, doc => doc.MethodRateLimitCount, Truncate(acc.MethodRateLimitCount, 128));
            SetIfPresent(updates, doc => doc.RateLimitType, Truncate(acc.RateLimitType, 32));
            // Host-level tag, stamped from config rather than carried per record.
            SetIfPresent(updates, doc => doc.ProcessName, Truncate(_options.ProcessName, 64));
            if (acc.RetryAfterSeconds is int retryAfter)
            {
                updates.Add(Update.Set(doc => doc.RetryAfterSeconds, retryAfter));
            }

            writes.Add(new UpdateOneModel<RiotApiCallRollupDocument>(filter, Update.Combine(updates))
            {
                IsUpsert = true
            });
        }

        return writes;
    }

    private static void SetIfPresent(
        List<UpdateDefinition<RiotApiCallRollupDocument>> updates,
        Expression<Func<RiotApiCallRollupDocument, string?>> field,
        string? value)
    {
        if (value is not null)
        {
            updates.Add(Update.Set(field, value));
        }
    }

    /// <summary>
    /// In-memory fold state for one <c>(minute, endpoint, statusCode)</c> group:
    /// running count and latency sum, the max timestamp, and the last-seen non-null
    /// value of each descriptive/rate-limit field.
    /// </summary>
    private sealed class Accumulator
    {
        public long Count { get; private set; }
        public long SumLatencyMs { get; private set; }
        public DateTime LastCalledAtUtc { get; private set; }
        public string? Method { get; private set; }
        public string? Route { get; private set; }
        public string? AppRateLimit { get; private set; }
        public string? AppRateLimitCount { get; private set; }
        public string? MethodRateLimit { get; private set; }
        public string? MethodRateLimitCount { get; private set; }
        public int? RetryAfterSeconds { get; private set; }
        public string? RateLimitType { get; private set; }

        public void Add(RiotApiCallRecord record)
        {
            Count++;
            SumLatencyMs += record.LatencyMs;
            if (record.TimestampUtc >= LastCalledAtUtc)
            {
                LastCalledAtUtc = record.TimestampUtc;
            }

            // Coalesce: keep the last non-null value seen for each field so a header
            // a call omitted doesn't drop one an earlier call in the batch provided.
            Method = record.Method ?? Method;
            Route = record.Route ?? Route;
            AppRateLimit = record.AppRateLimit ?? AppRateLimit;
            AppRateLimitCount = record.AppRateLimitCount ?? AppRateLimitCount;
            MethodRateLimit = record.MethodRateLimit ?? MethodRateLimit;
            MethodRateLimitCount = record.MethodRateLimitCount ?? MethodRateLimitCount;
            RetryAfterSeconds = record.RetryAfterSeconds ?? RetryAfterSeconds;
            RateLimitType = record.RateLimitType ?? RateLimitType;
        }
    }

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
