using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TrueMain.TestKit;

/// <summary>
/// A database of its own on the shared <see cref="PostgresFixture"/> container, for the tests
/// that have to drive the migrator themselves — replay the history up to an intermediate
/// migration, seed the shape it left, then migrate the rest.
/// </summary>
/// <remarks>
/// Doing that on the shared database meant dropping it behind the fixture's back and counting
/// on the final re-migration to hand the next test a complete schema (#1246). Here nothing the
/// test does can reach the database the rest of the suite runs on, and no second container is
/// started: the database is created empty by the first migration and dropped on disposal.
/// </remarks>
public sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly string _sharedConnectionString;
    private readonly string _name = $"truemain_scratch_{Guid.NewGuid():N}";
    private readonly NpgsqlDataSource _dataSource;

    internal ScratchDatabase(string sharedConnectionString)
    {
        _sharedConnectionString = sharedConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = _name }.ConnectionString;
        _dataSource = DataServiceCollectionExtensions.BuildDataSource(ConnectionString);
    }

    public string ConnectionString { get; }

    /// <summary>
    /// A context bound to the scratch database. The database does not exist until the first
    /// migration creates it.
    /// </summary>
    public TrueMainDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<TrueMainDbContext>().UseNpgsql(_dataSource).Options);

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();

        // Dropped from the shared database's connection, and with FORCE so a pooled
        // connection the test opened on its own cannot keep the scratch one alive.
        await using var connection = new NpgsqlConnection(_sharedConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE);", connection);
        await command.ExecuteNonQueryAsync();
    }
}
