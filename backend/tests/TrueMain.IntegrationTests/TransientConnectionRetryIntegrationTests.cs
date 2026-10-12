using AwesomeAssertions;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The API's retrying execution strategy against a real server-side failure (#1634):
/// the backend serving a pooled connection is terminated with
/// <c>pg_terminate_backend</c>, the way a Postgres or PgBouncer restart drops it, and
/// the next query lands on that dead connection.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TransientConnectionRetryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public TransientConnectionRetryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApiHost_RunsItsContextWithTheRetryingStrategy()
    {
        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrueMainDbContext>();

        db.Database.CreateExecutionStrategy().Should().BeOfType<TransientConnectionRetryStrategy>();
    }

    [Fact]
    public async Task TerminatedConnection_IsReplayed_WithTheRetryingStrategy()
    {
        var value = await QueryAfterTerminatingTheConnectionAsync(retryTransientFailures: true);

        value.Should().Be(1);
    }

    [Fact]
    public async Task TerminatedConnection_FailsTheQuery_WithoutTheRetryingStrategy()
    {
        // The control: the same injected failure surfaces when nothing retries, so the
        // test above passes because of the strategy and not because nothing broke.
        var act = () => QueryAfterTerminatingTheConnectionAsync(retryTransientFailures: false);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        (thrown as NpgsqlException ?? thrown.InnerException as NpgsqlException)
            .Should().NotBeNull("the failure is the dropped connection, got {0}", thrown);
    }

    private async Task<int> QueryAfterTerminatingTheConnectionAsync(bool retryTransientFailures)
    {
        // One physical connection, so the query after the termination is bound to reuse
        // the connector whose backend was killed.
        await using var dataSource = DataServiceCollectionExtensions.BuildDataSource(
            _fixture.ConnectionString + ";Maximum Pool Size=1");
        var options = new DbContextOptionsBuilder<TrueMainDbContext>();
        DataServiceCollectionExtensions.ConfigureNpgsql(options, dataSource, retryTransientFailures);
        await using var db = new TrueMainDbContext(options.Options);

        var backendPid = await ScalarAsync(db, "SELECT pg_backend_pid() AS \"Value\"");
        await TerminateAsync(backendPid);

        return await ScalarAsync(db, "SELECT 1 AS \"Value\"");
    }

    private async Task TerminateAsync(int backendPid)
    {
        await using var admin = _fixture.CreateDbContext();
        var terminated = await admin.Database
            .SqlQuery<bool>($"SELECT pg_terminate_backend({backendPid}, 5000) AS \"Value\"")
            .SingleAsync();
        terminated.Should().BeTrue("the pooled backend must be gone before the next query");
    }

    private static Task<int> ScalarAsync(TrueMainDbContext db, string sql)
        => db.Database.SqlQueryRaw<int>(sql).SingleAsync();
}
