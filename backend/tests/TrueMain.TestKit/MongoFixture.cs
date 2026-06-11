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
    }
}
