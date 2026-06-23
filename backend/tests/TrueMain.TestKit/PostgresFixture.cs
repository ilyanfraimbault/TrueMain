using System.Diagnostics.CodeAnalysis;
using Data;
using Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TrueMain.TestKit;

/// <summary>
/// Spawns a throwaway Postgres container, runs the migrations once, and
/// hands out <see cref="TrueMainDbContext"/> instances bound to it. Shared
/// across the whole integration test assembly via an xUnit collection fixture,
/// so a single container is started once and reused; tests reset data between
/// runs with <see cref="ResetDatabaseAsync"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.2")
        .WithDatabase("truemain_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        // Raise max_connections above the Postgres default (100). The whole
        // assembly shares one container, and every WebApplicationFactory a test
        // spins up opens its own Npgsql pool against it; a server-side backend
        // lingers briefly after a factory is disposed, so a fast back-to-back
        // run can transiently stack more than 100 connections and fail an
        // unrelated test's OpenAsync with a 500. The headroom keeps the suite
        // robust as it grows. (The entrypoint prepends `postgres` to a `-c …`
        // command, so the server still boots normally.)
        .WithCommand("-c", "max_connections=300")
        // Keep Testcontainers' Ryuk reaper disabled: its image
        // (testcontainers/ryuk) is not pullable in our CI / dev environment
        // (Docker Hub returns 401), so enabling cleanup makes the container
        // fail to start. The container-leak this fixture used to cause is now
        // addressed by sharing a single container per assembly (see
        // IntegrationCollection) instead of one per class; DisposeAsync removes
        // that single container on normal test-run completion.
        .WithCleanUp(false)
        .Build();
    private NpgsqlDataSource? _dataSource;
    private string? _truncateSql;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _dataSource = CreateDataSource();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    public TrueMainDbContext CreateDbContext()
    {
        var dataSource = _dataSource ??= CreateDataSource();
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        return new TrueMainDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();

        // The schema is migrated once in InitializeAsync; between tests only
        // the *data* needs to go. TRUNCATE ... RESTART IDENTITY CASCADE clears
        // every mapped table in a single statement — orders of magnitude
        // cheaper than dropping the database and replaying every migration on
        // each test — and CASCADE makes FK ordering irrelevant. The table list
        // comes from the EF model so new migrations are covered automatically.
        _truncateSql ??= BuildTruncateSql(db);
        await db.Database.ExecuteSqlRawAsync(_truncateSql);
    }

    private static string BuildTruncateSql(TrueMainDbContext db)
    {
        var tables = db.Model.GetEntityTypes()
            .Select(entity => (Schema: entity.GetSchema() ?? "public", Name: entity.GetTableName()))
            .Where(table => table.Name is not null)
            .Distinct()
            .Select(table => $"\"{table.Schema}\".\"{table.Name}\"");

        return $"TRUNCATE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;";
    }

    public IDataSessionFactory CreateSessionFactory()
        => new TestDataSessionFactory(this);

    private sealed class TestDataSessionFactory(PostgresFixture fixture) : IDataSessionFactory
    {
        private static readonly IDataRepositoryFactory RepositoryFactory = new DataRepositoryFactory();

        [SuppressMessage("Reliability", "CA2000", Justification = "Ownership is transferred to the caller via IDataSession.")]
        public Task<IDataSession> CreateAsync(CancellationToken ct)
        {
            var db = fixture.CreateDbContext();
            return Task.FromResult<IDataSession>(new DataSession(db, RepositoryFactory));
        }
    }

    private NpgsqlDataSource CreateDataSource()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        return dataSourceBuilder.Build();
    }
}
