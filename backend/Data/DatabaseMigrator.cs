using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Data;

/// <summary>
/// Applies any pending EF Core migrations against the configured
/// <see cref="TrueMainDbContext"/> when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/>
/// is enabled. Centralised so the API and the Ingestor share the exact same
/// startup behaviour rather than each rolling their own scope/context lookup.
/// </summary>
/// <remarks>
/// Runtime migration is intended for development and single-instance
/// deployments only. For production, keep
/// <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> disabled and apply
/// idempotent SQL scripts during deployment instead. See
/// <c>docs/production-migrations.md</c> for the recommended path.
/// </remarks>
public static class DatabaseMigrator
{
    public static async Task ApplyPendingMigrationsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(DatabaseMigrator).FullName!)
            ?? NullLogger.Instance;

        if (!options.ApplyMigrationsOnStartup)
        {
            logger.LogInformation(
                "[db-migrator] skipped: ApplyMigrationsOnStartup is disabled. " +
                "Apply migrations out-of-band (see docs/production-migrations.md).");
            return;
        }

        await using var scope = services.CreateAsyncScope();

        // EF Core takes a database lock while migrating (a session-scoped Postgres
        // advisory lock), which does NOT survive PgBouncer transaction pooling:
        // the session is not pinned across statements, so the lock could be taken
        // and released on different backends. When a dedicated direct-to-Postgres
        // connection string is configured (ConnectionStrings:TrueMainMigrations),
        // migrate over it so the lock lives on a stable session; the app keeps
        // using the pooled (pgbouncer) connection for its runtime queries. Falls
        // back to the DI-registered context when unset — dev, tests and any
        // single-instance setup with no pooler in front of Postgres.
        var migrationsConnectionString = services
            .GetService<IConfiguration>()
            ?.GetConnectionString("TrueMainMigrations");

        if (!string.IsNullOrWhiteSpace(migrationsConnectionString))
        {
            // Mirror the hosts' data source wiring (dynamic JSON) so the migration
            // context maps the same types as the runtime one. We own both the
            // data source and the context here, so `await using` disposes them.
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(migrationsConnectionString);
            dataSourceBuilder.EnableDynamicJson();
            await using var dataSource = dataSourceBuilder.Build();

            var contextOptions = new DbContextOptionsBuilder<TrueMainDbContext>()
                .UseNpgsql(dataSource)
                .Options;
            await using var context = new TrueMainDbContext(contextOptions);
            await MigrateAsync(context, logger, cancellationToken);
            return;
        }

        // A scoped context, when registered, is owned and disposed by the scope.
        var scopedContext = scope.ServiceProvider.GetService<TrueMainDbContext>();
        if (scopedContext is not null)
        {
            await MigrateAsync(scopedContext, logger, cancellationToken);
            return;
        }

        // Some hosts (notably the Ingestor) only register a DbContextFactory; a
        // factory-created context is ours to dispose.
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TrueMainDbContext>>();
        await using var factoryContext = await factory.CreateDbContextAsync(cancellationToken);
        await MigrateAsync(factoryContext, logger, cancellationToken);
    }

    private static async Task MigrateAsync(
        TrueMainDbContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("[db-migrator] applying pending migrations on startup.");
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("[db-migrator] migrations applied successfully.");
        }
        catch (Exception ex)
        {
            // Re-throw so the host still fails fast, but emit context first so
            // operators are not left with a bare crash and no explanation.
            logger.LogError(
                ex,
                "[db-migrator] failed to apply pending migrations on startup. " +
                "If this is production, disable ApplyMigrationsOnStartup and apply " +
                "migrations via an idempotent SQL script (see docs/production-migrations.md).");
            throw;
        }
    }
}
