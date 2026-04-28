using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Data;

/// <summary>
/// Applies any pending EF Core migrations against the configured
/// <see cref="TrueMainDbContext"/> when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/>
/// is enabled. Centralised so the API and the Ingestor share the exact same
/// startup behaviour rather than each rolling their own scope/context lookup.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task ApplyPendingMigrationsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();

        // Some hosts (notably the Ingestor) only register a DbContextFactory.
        // Resolve through the factory when no scoped context is available so
        // both wiring styles work.
        var context = scope.ServiceProvider.GetService<TrueMainDbContext>();
        if (context is not null)
        {
            await context.Database.MigrateAsync(cancellationToken);
            return;
        }

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TrueMainDbContext>>();
        await using var scopedContext = await factory.CreateDbContextAsync(cancellationToken);
        await scopedContext.Database.MigrateAsync(cancellationToken);
    }
}
