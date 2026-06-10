using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>
/// Singleton holder of the shared <see cref="IMongoClient"/> and the two log
/// collections (diagnostic <c>logs</c> and operator-action <c>audit_events</c>).
/// Registered once so the diagnostic sink, the audit writer and the read query
/// all reuse the same driver client (which owns an internal connection pool).
/// </summary>
/// <remarks>
/// The client is created lazily and only when logging
/// <see cref="MongoLoggingOptions.IsActive"/>; when inactive
/// <see cref="IsActive"/> is false and the collection accessors throw, so callers
/// must gate on it (the sink/audit writer do).
/// </remarks>
public sealed class MongoLogContext
{
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
    }

    /// <summary>True when a Mongo client was created (logging enabled + connection string present).</summary>
    public bool IsActive => _database is not null;

    public IMongoCollection<MongoLogDocument> Logs =>
        Require().GetCollection<MongoLogDocument>(_options.LogsCollection);

    public IMongoCollection<AuditEventDocument> AuditEvents =>
        Require().GetCollection<AuditEventDocument>(_options.AuditCollection);

    /// <summary>
    /// Creates the supporting indexes idempotently: a TTL index on the diagnostic
    /// <c>logs</c> collection (when a retention window is configured) plus the
    /// equality/range indexes backing the <c>/ops/logs</c> filters, and a
    /// timestamp index on <c>audit_events</c> for newest-first audit reads. Mongo
    /// <c>CreateOne</c> is a no-op when an identical index already exists, so this
    /// is safe to call on every startup.
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
                new CreateIndexOptions { Name = "ix_category" })
        };

        if (_options.LogsRetention > TimeSpan.Zero)
        {
            // Native TTL index: Mongo's background reaper deletes documents whose
            // timestampUtc is older than the configured window, replacing the
            // never-built LogRetentionProcess. Ascending key is required for TTL.
            logModels.Add(new CreateIndexModel<MongoLogDocument>(
                Builders<MongoLogDocument>.IndexKeys.Ascending(doc => doc.TimestampUtc),
                new CreateIndexOptions
                {
                    Name = "ttl_timestamp",
                    ExpireAfter = _options.LogsRetention
                }));
        }

        await Logs.Indexes.CreateManyAsync(logModels, ct);

        // audit_events: no TTL (retained indefinitely). A descending timestamp
        // index backs the newest-first audit read.
        await AuditEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditEventDocument>(
                Builders<AuditEventDocument>.IndexKeys.Descending(doc => doc.TimestampUtc),
                new CreateIndexOptions { Name = "ix_timestamp_desc" }),
            cancellationToken: ct);
    }

    private IMongoDatabase Require() =>
        _database ?? throw new InvalidOperationException(
            "MongoLogContext is inactive: MongoLogging is disabled or has no ConnectionString. " +
            "Gate on IsActive before accessing collections.");
}
