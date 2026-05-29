using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

        // Some hosts (notably the Ingestor) only register a DbContextFactory.
        // Resolve through the factory when no scoped context is available so
        // both wiring styles work.
        var context = scope.ServiceProvider.GetService<TrueMainDbContext>();

        // The scope owns and disposes the scoped instance; a factory-created
        // context is ours to dispose. Track which path produced the context so
        // the finally block disposes exactly what it must.
        var contextOwnedHere = context is null;
        if (context is null)
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TrueMainDbContext>>();
            context = await factory.CreateDbContextAsync(cancellationToken);
        }

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
        finally
        {
            if (contextOwnedHere)
            {
                await context.DisposeAsync();
            }
        }
    }
}
