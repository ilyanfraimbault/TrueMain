using System.Net.Sockets;
using AwesomeAssertions;
using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TrueMain.UnitTests;

/// <summary>
/// What the API's retrying execution strategy replays and what it lets through (#1634).
/// The operations never reach a database: they throw the exception Npgsql would.
/// </summary>
public sealed class TransientConnectionRetryStrategyTests : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        DataServiceCollectionExtensions.BuildDataSource("Host=localhost;Database=unused");

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    [Fact]
    public void RetryingWiring_UsesTheStrategy_AndTheDefaultWiringDoesNot()
    {
        using var retrying = CreateContext(retryTransientFailures: true);
        using var plain = CreateContext(retryTransientFailures: false);

        retrying.Database.CreateExecutionStrategy().Should().BeOfType<TransientConnectionRetryStrategy>();
        plain.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();
    }

    public static TheoryData<string> TransientFailures => ["broken-socket", "io", "57P01", "53300"];

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task TransientFailure_IsReplayed_UntilTheQuerySucceeds(string failure)
    {
        await using var context = CreateContext(retryTransientFailures: true);
        var attempts = 0;

        var result = await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            attempts++;
            return attempts == 1 ? throw CreateFailure(failure) : Task.FromResult(42);
        });

        result.Should().Be(42);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ClientTimeout_IsNotReplayed()
    {
        await using var context = CreateContext(retryTransientFailures: true);
        var attempts = 0;
        var timeout = new NpgsqlException("Exception while reading from stream", new TimeoutException());
        timeout.IsTransient.Should().BeTrue("Npgsql flags it transient, so the override is what stops the replay");

        var act = () => context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException<int>(timeout);
        });

        (await act.Should().ThrowAsync<NpgsqlException>()).Which.Should().BeSameAs(timeout);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task NonTransientServerError_IsNotReplayed()
    {
        await using var context = CreateContext(retryTransientFailures: true);
        var attempts = 0;

        var act = () => context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException<int>(CreatePostgresException("42P01"));
        });

        await act.Should().ThrowAsync<PostgresException>();
        attempts.Should().Be(1);
    }

    private TrueMainDbContext CreateContext(bool retryTransientFailures)
    {
        var options = new DbContextOptionsBuilder<TrueMainDbContext>();
        DataServiceCollectionExtensions.ConfigureNpgsql(options, _dataSource, retryTransientFailures);
        return new TrueMainDbContext(options.Options);
    }

    private static Exception CreateFailure(string failure) => failure switch
    {
        "broken-socket" => new NpgsqlException("Exception while writing to stream", new SocketException()),
        "io" => new NpgsqlException("Exception while reading from stream", new IOException()),
        _ => CreatePostgresException(failure),
    };

    private static PostgresException CreatePostgresException(string sqlState)
        => new("simulated", "FATAL", "FATAL", sqlState);
}
