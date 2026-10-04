using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Data;

/// <summary>
/// Runs <see cref="DatabaseMigrator.ApplyPendingMigrationsAsync"/> as part of the host's
/// startup (#258), instead of as an ad-hoc call between <c>Build</c> and <c>Run</c>.
///
/// <para>
/// As a hosted service the migration is part of the host lifecycle: it receives the startup
/// cancellation token (a SIGTERM during a long migration aborts it instead of being ignored
/// until it finishes), and a failure fails <c>StartAsync</c>, which surfaces from
/// <c>RunAsync</c> into the host's crash capture exactly like the previous call did.
/// </para>
///
/// <para>
/// <b>An <see cref="IHostedService"/> whose work is all in <c>StartAsync</c>.</b> Hosted
/// services start sequentially in registration order, and the web server only starts after
/// every one of them, so registering this ahead of anything that reads the schema (the
/// ingestor's worker, the API's endpoints) guarantees the schema is current before it is used.
/// </para>
///
/// <para>
/// The <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> gate is unchanged: with it
/// disabled (production and preprod, which migrate out-of-band) this logs a skip and returns.
/// </para>
/// </summary>
public sealed class DatabaseMigrationHostedService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        DatabaseMigrator.ApplyPendingMigrationsAsync(services, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Registration helper for <see cref="DatabaseMigrationHostedService"/>.
/// </summary>
public static class DatabaseMigrationServiceCollectionExtensions
{
    /// <summary>
    /// Applies pending migrations at host startup when
    /// <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is enabled. Call it before
    /// registering any hosted service that touches the database: hosted services start in
    /// registration order.
    /// </summary>
    public static IServiceCollection AddDatabaseMigrationsOnStartup(this IServiceCollection services)
    {
        services.AddHostedService<DatabaseMigrationHostedService>();

        return services;
    }
}
