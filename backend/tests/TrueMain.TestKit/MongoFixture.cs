using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace TrueMain.TestKit;

/// <summary>
/// Spawns a throwaway MongoDB container for the integration tests that exercise
/// the log store / audit writer (logs moved off Postgres in #416). Shared across
/// the integration test assembly via an xUnit collection fixture so a single
/// container is started once and reused; tests clear collections between runs
/// with <see cref="ResetAsync"/>.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    public const string DatabaseName = "truemain_logs_test";
    public const string LogsCollection = "logs";
    public const string AuditCollection = "audit_events";
    public const string RiotApiCallsCollection = "riot_api_call_rollups";
    public const string ProcessRunsCollection = "process_runs";
    public const string SeedRequestsCollection = "seed_requests";
    public const string DbTableSizeSnapshotsCollection = "db_table_size_snapshots";

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
        // Match PostgresFixture's reasoning: keep Testcontainers' Ryuk reaper
        // disabled (its image is not always pullable in CI), relying on the
        // single-container-per-assembly share + DisposeAsync for cleanup.
        .WithCleanUp(false)
        .Build();

    private IMongoClient? _client;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = new MongoClient(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        // Dispose the driver client first so its connection pool and background
        // monitoring threads are torn down before the container goes away.
        if (_client is IDisposable d)
        {
            d.Dispose();
        }

        await _container.DisposeAsync();
    }

    public IMongoDatabase GetDatabase() =>
        (_client ??= new MongoClient(ConnectionString)).GetDatabase(DatabaseName);

    public IMongoCollection<TDocument> GetCollection<TDocument>(string name) =>
        GetDatabase().GetCollection<TDocument>(name);

    /// <summary>
    /// Drops the log + audit collections so each test starts from a clean slate.
    /// Dropping (rather than deleting documents) also clears any indexes a prior
    /// test's sink created, keeping behaviour deterministic.
    /// </summary>
    public async Task ResetAsync()
    {
        var db = GetDatabase();
        await db.DropCollectionAsync(LogsCollection);
        await db.DropCollectionAsync(AuditCollection);
        await db.DropCollectionAsync(RiotApiCallsCollection);
        await db.DropCollectionAsync(ProcessRunsCollection);
        await db.DropCollectionAsync(SeedRequestsCollection);
        // Dropped like the rest, and it matters more here: the collection carries a
        // unique index the storage-snapshot tests reconcile, so a leaked index would
        // make them pass or fail depending on run order.
        await db.DropCollectionAsync(DbTableSizeSnapshotsCollection);
    }
}
