using System.Linq.Expressions;
using Data.Metrics.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>
/// Singleton holder of the shared <see cref="IMongoClient"/> and the Mongo
/// collections backing TrueMain's observability stores: the diagnostic
/// <c>logs</c>, the operator-action <c>audit_events</c>, and the Riot API usage
/// metrics <c>riot_api_calls</c> (#93). Registered once so every sink, writer and
/// read query reuses the same driver client (which owns an internal connection
/// pool).
/// </summary>
/// <remarks>
/// The client is created lazily and only when logging
/// <see cref="MongoLoggingOptions.IsActive"/>; when inactive
/// <see cref="IsActive"/> is false and the collection accessors throw, so callers
/// must gate on it (the sink/audit writer do).
/// </remarks>
public sealed class MongoLogContext : IDisposable
{
    private const string TtlIndexName = "ttl_timestamp";

    private readonly MongoLoggingOptions _options;
    private readonly IMongoClient? _client;
    private readonly IMongoDatabase? _database;

    public MongoLogContext(IOptions<MongoLoggingOptions> options)
    {
        _options = options.Value;

        if (!_options.IsActive)
        {
            return;
        }

        _client = new MongoClient(_options.ConnectionString);
        _database = _client.GetDatabase(_options.Database);
        Logs = _database.GetCollection<MongoLogDocument>(_options.LogsCollection);
        AuditEvents = _database.GetCollection<AuditEventDocument>(_options.AuditCollection);
        RiotApiCallRollups = _database.GetCollection<RiotApiCallRollupDocument>(_options.RiotApiCallsCollection);
    }

    /// <summary>True when a Mongo client was created (logging enabled + connection string present).</summary>
    public bool IsActive => _database is not null;

    // The collection wrappers are cached in the property backing field (the `field`
    // keyword): GetCollection<T>() allocates a fresh wrapper on every call, and the
    // collections are hit per flush (MongoLogSink) and several times in
    // EnsureIndexesAsync. The backing field stays null when the context is inactive,
    // so the getter throws to keep the inactive guard.
    public IMongoCollection<MongoLogDocument> Logs
    {
        get => field ?? throw Inactive();
        private init;
    }

    public IMongoCollection<AuditEventDocument> AuditEvents
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// Per-minute Riot API usage rollup collection (#93). Written by the Ingestor's
    /// <c>RiotApiMetricsSink</c> (one <c>$inc</c>-upsert per minute/endpoint/status)
    /// and read by the admin <c>/ops/riot-usage</c> panel.
    /// </summary>
    public IMongoCollection<RiotApiCallRollupDocument> RiotApiCallRollups
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// Creates the supporting indexes idempotently: a TTL index on the diagnostic
    /// <c>logs</c> collection (when a retention window is configured) plus the
    /// equality/range indexes backing the <c>/ops/logs</c> filters, and a
    /// timestamp index on <c>audit_events</c> for newest-first audit reads. Mongo
    /// <c>CreateMany</c>/<c>CreateOne</c> is a no-op when an *identical* index
    /// already exists, so the non-TTL indexes are safe to recreate on every
    /// startup.
    /// <para>
    /// The TTL index is special: re-issuing it with a different
    /// <c>expireAfterSeconds</c> would throw <c>IndexOptionsConflict</c> (a
    /// same-name/keys index with different options), which the sink swallows — so a
    /// changed <see cref="MongoLoggingOptions.LogsRetention"/> would silently never
    /// take effect. To make a retention change apply, this reconciles the TTL index
    /// explicitly (see <see cref="ReconcileTtlIndexAsync"/>): it reads the existing
    /// index's <c>expireAfterSeconds</c> and, when it differs from the configured
    /// window, drops and recreates the index.
    /// </para>
    /// </summary>
    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (!IsActive)
        {
            return;
        }

        var logModels = new List<CreateIndexModel<MongoLogDocument>>
        {
            // Newest-first listing is the dominant read on /ops/logs; a descending
            // index on the timestamp serves the default page without a sort.
            new(Builders<MongoLogDocument>.IndexKeys.Descending(doc => doc.TimestampUtc),
                new CreateIndexOptions { Name = "ix_timestamp_desc" }),
            // Back the level / category equality + prefix filters.
            new(Builders<MongoLogDocument>.IndexKeys.Ascending(doc => doc.Level),
                new CreateIndexOptions { Name = "ix_level" }),
            new(Builders<MongoLogDocument>.IndexKeys.Ascending(doc => doc.Category),
                new CreateIndexOptions { Name = "ix_category" }),
            // Back the /ops/logs eventType filter. Sparse: only ops-event rows
            // carry the field (the overwhelming majority of diagnostics don't),
            // so the index stays tiny while sparing event-filtered reads a full
            // collection scan.
            new(Builders<MongoLogDocument>.IndexKeys.Ascending(doc => doc.EventType),
                new CreateIndexOptions { Name = "ix_event_type", Sparse = true })
        };

        await Logs.Indexes.CreateManyAsync(logModels, ct);

        // The TTL index is reconciled separately so a changed retention window
        // actually re-applies instead of conflicting and being silently swallowed.
        await ReconcileTtlIndexAsync(Logs, doc => doc.TimestampUtc, _options.LogsRetention, ct);

        // audit_events: no TTL (retained indefinitely). A descending timestamp
        // index backs the newest-first audit read.
        await AuditEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditEventDocument>(
                Builders<AuditEventDocument>.IndexKeys.Descending(doc => doc.TimestampUtc),
                new CreateIndexOptions { Name = "ix_timestamp_desc" }),
            cancellationToken: ct);
    }

    /// <summary>
    /// Creates the supporting indexes for the <c>riot_api_call_rollups</c> metrics
    /// collection idempotently (#93): a unique <c>(bucketStartUtc, endpoint,
    /// statusCode)</c> compound so each <c>$inc</c>-upsert targets exactly one
    /// rollup, a descending <c>bucketStartUtc</c> index for window scans, an
    /// <c>(endpoint, bucketStartUtc)</c> compound for the per-endpoint filter, and
    /// the reconciled TTL index on <c>bucketStartUtc</c> enforcing
    /// <see cref="MongoLoggingOptions.RiotApiCallsRetention"/>. Called once on
    /// <c>RiotApiMetricsSink</c> startup; safe to re-run.
    /// </summary>
    public async Task EnsureRiotApiCallIndexesAsync(CancellationToken ct)
    {
        if (!IsActive)
        {
            return;
        }

        var models = new List<CreateIndexModel<RiotApiCallRollupDocument>>
        {
            // The upsert key: one rollup per minute/endpoint/status. Unique so a
            // concurrency race can't split a bucket into duplicate documents.
            new(Builders<RiotApiCallRollupDocument>.IndexKeys
                    .Ascending(doc => doc.BucketStartUtc)
                    .Ascending(doc => doc.Endpoint)
                    .Ascending(doc => doc.StatusCode),
                new CreateIndexOptions { Name = "ux_bucket_endpoint_status", Unique = true }),
            // The window lower bound (bucketStartUtc >= since) is on every read; a
            // descending index serves it and the newest-first rate-limit lookup.
            new(Builders<RiotApiCallRollupDocument>.IndexKeys.Descending(doc => doc.BucketStartUtc),
                new CreateIndexOptions { Name = "ix_timestamp_desc" }),
            // Back the optional endpoint filter: scan one endpoint within a window.
            new(Builders<RiotApiCallRollupDocument>.IndexKeys
                    .Ascending(doc => doc.Endpoint)
                    .Descending(doc => doc.BucketStartUtc),
                new CreateIndexOptions { Name = "ix_endpoint_timestamp" })
        };

        await RiotApiCallRollups.Indexes.CreateManyAsync(models, ct);

        await ReconcileTtlIndexAsync(
            RiotApiCallRollups, doc => doc.BucketStartUtc, _options.RiotApiCallsRetention, ct);
    }

    /// <summary>
    /// Reconciles the native TTL index on <paramref name="collection"/>'s
    /// timestamp field with the configured <paramref name="retention"/> window
    /// (e.g. <see cref="MongoLoggingOptions.LogsRetention"/> for <c>logs</c>,
    /// <see cref="MongoLoggingOptions.RiotApiCallsRetention"/> for
    /// <c>riot_api_calls</c>):
    /// <list type="bullet">
    /// <item>retention &lt;= 0 → drop the TTL index if present (retain indefinitely);</item>
    /// <item>no TTL index yet → create it;</item>
    /// <item>TTL index exists with a different <c>expireAfterSeconds</c> → drop and
    /// recreate so the new window takes effect (re-creating with the same name and
    /// different options would otherwise throw <c>IndexOptionsConflict</c>);</item>
    /// <item>TTL index already matches → no-op.</item>
    /// </list>
    /// Mongo's background reaper then deletes documents whose <c>timestampUtc</c> is
    /// older than the window. Ascending key is required for a TTL index.
    /// </summary>
    private static async Task ReconcileTtlIndexAsync<TDoc>(
        IMongoCollection<TDoc> collection,
        Expression<Func<TDoc, object?>> timestampField,
        TimeSpan retention,
        CancellationToken ct)
    {
        var existing = await GetTtlExpireAfterSecondsAsync(collection, ct);

        if (retention <= TimeSpan.Zero)
        {
            // Retention disabled: tear down any TTL index left from a prior config
            // so documents are kept indefinitely.
            if (existing is not null)
            {
                await collection.Indexes.DropOneAsync(TtlIndexName, ct);
            }

            return;
        }

        var desiredSeconds = (long)retention.TotalSeconds;

        // Already present with the same window: nothing to do.
        if (existing == desiredSeconds)
        {
            return;
        }

        // Present but with a stale window: drop it first, since re-creating a
        // same-name index with different options would throw IndexOptionsConflict.
        if (existing is not null)
        {
            await collection.Indexes.DropOneAsync(TtlIndexName, ct);
        }

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<TDoc>(
                Builders<TDoc>.IndexKeys.Ascending(timestampField),
                new CreateIndexOptions
                {
                    Name = TtlIndexName,
                    ExpireAfter = retention
                }),
            cancellationToken: ct);
    }

    /// <summary>
    /// Returns the <c>expireAfterSeconds</c> of the existing TTL index on
    /// <paramref name="collection"/>, or <c>null</c> when no such index exists.
    /// Reads the raw index document so it works regardless of how the value was
    /// originally written.
    /// </summary>
    private static async Task<long?> GetTtlExpireAfterSecondsAsync<TDoc>(
        IMongoCollection<TDoc> collection,
        CancellationToken ct)
    {
        using var cursor = await collection.Indexes.ListAsync(ct);
        var indexes = await cursor.ToListAsync(ct);

        var ttl = indexes.FirstOrDefault(
            index => index.TryGetValue("name", out var name)
                     && name.IsString
                     && name.AsString == TtlIndexName);

        if (ttl is null || !ttl.TryGetValue("expireAfterSeconds", out var expire))
        {
            return null;
        }

        // expireAfterSeconds is typically stored as an Int32/Int64; ToInt64 handles
        // either numeric representation.
        return expire.ToInt64();
    }

    private static InvalidOperationException Inactive() =>
        new("MongoLogContext is inactive: MongoLogging is disabled or has no ConnectionString. " +
            "Gate on IsActive before accessing collections.");

    /// <summary>
    /// Disposes the owned <see cref="IMongoClient"/> so the driver's connection
    /// pool and background monitoring threads are torn down (CA2213). The DI
    /// container disposes this singleton on host shutdown.
    /// </summary>
    public void Dispose()
    {
        // MongoClient implements IDisposable in the modern driver; guard with a
        // pattern match so we stay correct even if the abstraction doesn't.
        if (_client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
