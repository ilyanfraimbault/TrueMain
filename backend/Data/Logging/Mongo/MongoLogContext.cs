using Data.Logging.Crash;
using Data.Metrics.Mongo;
using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>
/// Singleton holder of the shared <see cref="IMongoClient"/> and the Mongo
/// collections backing TrueMain's observability stores: the diagnostic
/// <c>logs</c>, the operator-action <c>audit_events</c>, and the Riot API usage
/// rollups <c>riot_api_call_rollups</c> (#93). Registered once so every sink, writer and
/// read query reuses the same driver client (which owns an internal connection
/// pool).
/// </summary>
/// <remarks>
/// The client is created lazily and only when logging
/// <see cref="MongoLoggingOptions.IsActive"/>; when inactive
/// <see cref="IsActive"/> is false and the collection accessors throw, so callers
/// must gate on it (the sink/audit writer do). Each collection's index bootstrap
/// lives in <c>MongoLogContext.Indexes.cs</c>.
/// </remarks>
public sealed partial class MongoLogContext : IDisposable
{
    /// <summary>The options this context was built from, for the stores that keep their own collection.</summary>
    internal MongoLoggingOptions Options { get; }
    private readonly IMongoClient? _client;
    private readonly IMongoDatabase? _database;

    public MongoLogContext(IOptions<MongoLoggingOptions> options)
    {
        Options = options.Value;

        if (!Options.IsActive)
        {
            return;
        }

        _client = new MongoClient(Options.ConnectionString);
        _database = _client.GetDatabase(Options.Database);
        Logs = _database.GetCollection<MongoLogDocument>(Options.LogsCollection);
        AuditEvents = _database.GetCollection<AuditEventDocument>(Options.AuditCollection);
        RiotApiCallRollups = _database.GetCollection<RiotApiCallRollupDocument>(Options.RiotApiCallsCollection);
        Crashes = _database.GetCollection<CrashReportDocument>(Options.CrashesCollection);
        DbTableSizeSnapshots =
            _database.GetCollection<DbTableSizeSnapshotDocument>(Options.DbTableSizeSnapshotsCollection);
        CandidateStockSnapshots =
            _database.GetCollection<CandidateStockSnapshotDocument>(Options.CandidateStockSnapshotsCollection);
        ProcessRuns = _database.GetCollection<ProcessRunDocument>(Options.ProcessRunsCollection);
        SeedRequests = _database.GetCollection<SeedRequestDocument>(Options.SeedRequestsCollection);
        EffectiveConfigurations = _database.GetCollection<EffectiveConfigurationDocument>(
            Options.EffectiveConfigurationCollection);
    }

    /// <summary>True when a Mongo client was created (logging enabled + connection string present).</summary>
    public bool IsActive => _database is not null;

    /// <summary>
    /// The raw database handle, for callers that need an admin command or a collection
    /// this context does not model: the storage-stats reader's <c>dbStats</c> (#1023) and
    /// the process-iteration headers (#2124), which keep their own collection and options.
    /// </summary>
    internal IMongoDatabase Database => _database ?? throw Inactive();

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
    /// Lossless crash reports (one per process crash, #crash-logs), written
    /// synchronously by <c>CrashReporter</c> and read by the admin Crashes panel.
    /// </summary>
    public IMongoCollection<CrashReportDocument> Crashes
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// Daily per-table Postgres storage snapshots (#925), written by the Ingestor's
    /// storage-snapshot step (one day-keyed upsert per table) and read by the admin
    /// database panel's growth charts and disk forecast.
    /// </summary>
    public IMongoCollection<DbTableSizeSnapshotDocument> DbTableSizeSnapshots
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// Hourly candidate-stock snapshots (#1403), written by the Ingestor's
    /// candidate-stock snapshot step (one hour-keyed upsert per platform and status)
    /// and read by the admin candidates panel to chart the funnel's level over time.
    /// </summary>
    public IMongoCollection<CandidateStockSnapshotDocument> CandidateStockSnapshots
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// Recorded ingestor process runs, written by the Ingestor's
    /// <c>ProcessRunRecorder</c> and read by the admin process panels.
    /// </summary>
    public IMongoCollection<ProcessRunDocument> ProcessRuns
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// The "seed by Riot ID" intake queue (#409): written by the API, claimed and
    /// resolved by the Ingestor's <c>ManualSeedProcess</c>, read by the admin.
    /// </summary>
    public IMongoCollection<SeedRequestDocument> SeedRequests
    {
        get => field ?? throw Inactive();
        private init;
    }

    /// <summary>
    /// One effective-configuration snapshot per process (#1034): published by the Ingestor at
    /// each boot, read by the Api to serve the admin configuration page. The Api's own half of
    /// that page is built live from its container and never goes through here.
    /// </summary>
    public IMongoCollection<EffectiveConfigurationDocument> EffectiveConfigurations
    {
        get => field ?? throw Inactive();
        private init;
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
