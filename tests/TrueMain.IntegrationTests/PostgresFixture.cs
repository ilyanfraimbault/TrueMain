using Data;
using Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics.CodeAnalysis;
using Testcontainers.PostgreSql;

namespace TrueMain.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.2")
        .WithDatabase("truemain_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(false)
        .Build();
    private NpgsqlDataSource? _dataSource;

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
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public IDataSessionFactory CreateSessionFactory()
    {
        return new TestDataSessionFactory(this);
    }

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
