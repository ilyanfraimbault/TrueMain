using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Data;

/// <summary>
/// The retrying execution strategy the API runs its <see cref="TrueMainDbContext"/>
/// with (#1634): it replays a query that failed on an error Npgsql flags as transient
/// (a connection reset, a PgBouncer or Postgres restart surfacing as <c>57P01</c>,
/// <c>53300</c> too many connections) a bounded number of times, so a blip does not
/// turn into a failed request.
/// </summary>
/// <remarks>
/// <para>
/// Client-side timeouts are deliberately <b>not</b> retried, although Npgsql flags
/// them as transient: a command that ran into <c>Command Timeout</c> will run into it
/// again, so a replay only multiplies the load on a database that is already slow and
/// stretches one request to several times the timeout. The same applies to an
/// exhausted connection pool, which Npgsql also reports as a timeout.
/// </para>
/// <para>
/// A retrying strategy rejects user-initiated transactions that are not wrapped in
/// <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>. The API runs none —
/// its Postgres access is read-only — which is what makes it safe there. The Ingestor
/// keeps the default non-retrying strategy: its explicit transactions are not
/// replayable units of work, and a failed pass already replays on the next cycle.
/// </para>
/// </remarks>
public sealed class TransientConnectionRetryStrategy(ExecutionStrategyDependencies dependencies)
    : NpgsqlRetryingExecutionStrategy(dependencies, MaxRetries, MaxDelay, errorCodesToAdd: null)
{
    /// <summary>Attempts after the first one, so at most four executions per query.</summary>
    public const int MaxRetries = 3;

    /// <summary>Cap on the exponential backoff between two attempts.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception? exception)
        => exception is not null && !IsClientTimeout(exception) && base.ShouldRetryOn(exception);

    /// <summary>
    /// True when <paramref name="exception"/> is Npgsql reporting a client-side timeout
    /// (command timeout or connection-pool exhaustion) rather than a broken connection.
    /// </summary>
    public static bool IsClientTimeout(Exception exception)
        => exception is NpgsqlException { InnerException: TimeoutException };
}
