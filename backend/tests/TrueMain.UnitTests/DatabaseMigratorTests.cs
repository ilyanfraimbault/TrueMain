using AwesomeAssertions;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Verifies that <see cref="DatabaseMigrator.ApplyPendingMigrationsAsync"/>
/// honours <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/>, so a
/// production host (flag disabled) never auto-migrates on startup.
/// </summary>
public sealed class DatabaseMigratorTests
{
    [Fact]
    public async Task ApplyPendingMigrationsAsync_does_nothing_when_flag_disabled()
    {
        var services = BuildServiceProvider(applyOnStartup: false);

        var act = async () => await DatabaseMigrator.ApplyPendingMigrationsAsync(services);

        // With the flag disabled the migrator must short-circuit before it ever
        // resolves a context. If it instead reached the resolution path it would
        // surface the sentinel exception below, so completing cleanly proves the
        // gate held.
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ApplyPendingMigrationsAsync_resolves_context_when_flag_enabled()
    {
        var services = BuildServiceProvider(applyOnStartup: true);

        var act = async () => await DatabaseMigrator.ApplyPendingMigrationsAsync(services);

        // With the flag enabled the migrator must move past the gate and attempt
        // to resolve a context; the sentinel factory throws to prove it got there
        // without needing a real database.
        await act.Should().ThrowAsync<MigratorReachedException>();
    }

    private static ServiceProvider BuildServiceProvider(bool applyOnStartup)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<DatabaseOptions>>(
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = applyOnStartup }));

        // The migrator resolves IConfiguration to look up the optional
        // direct-to-Postgres migrations connection string. An empty config leaves
        // it unset, so the migrator falls through to the factory path below.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // Register only the factory (the Ingestor wiring style). Creating a
        // context throws so the test never touches a real database while still
        // proving whether the migrator reached the resolution path.
        services.AddSingleton<IDbContextFactory<TrueMainDbContext>, ThrowingDbContextFactory>();

        return services.BuildServiceProvider();
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<TrueMainDbContext>
    {
        public TrueMainDbContext CreateDbContext() => throw new MigratorReachedException();
    }

    private sealed class MigratorReachedException : Exception
    {
    }
}
