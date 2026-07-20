using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Data;

/// <summary>
/// Single wiring point for the TrueMain PostgreSQL data access, shared by every
/// host (API, Ingestor), the test fixture, and the design-time factory. Building
/// the <see cref="NpgsqlDataSource"/> here — with <c>EnableDynamicJson</c> so the
/// jsonb columns (item/skill events, breakdowns, summaries, starter items) map
/// the same way everywhere — keeps a single source of truth instead of the
/// previously duplicated <see cref="NpgsqlDataSourceBuilder"/> setup.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared <see cref="NpgsqlDataSource"/> and the
    /// <see cref="TrueMainDbContext"/> on <paramref name="services"/>.
    /// <see cref="EntityFrameworkServiceCollectionExtensions.AddDbContextFactory{TContext}(IServiceCollection, System.Action{DbContextOptionsBuilder}, ServiceLifetime)"/>
    /// registers both the <c>IDbContextFactory&lt;TrueMainDbContext&gt;</c> (for
    /// services that fan out independently owned contexts across parallel branches)
    /// and the context itself as a scoped service (for the common request-scoped
    /// injection), so it is the only registration the hosts need.
    /// </summary>
    public static IServiceCollection AddTrueMainData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetRequiredConnectionString(configuration);

        // Registered as a singleton so the pool and its type mappings are shared
        // process-wide and the source is disposed with the container.
        services.AddSingleton(_ => BuildDataSource(connectionString));
        services.AddDbContextFactory<TrueMainDbContext>(
            (serviceProvider, options) => options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>()));

        return services;
    }

    /// <summary>
    /// Builds an <see cref="NpgsqlDataSource"/> with <c>EnableDynamicJson</c> for
    /// callers that own the context directly (the design-time factory, the
    /// dedicated-migration connection, the integration-test fixture) rather than
    /// through dependency injection.
    /// </summary>
    public static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        return dataSourceBuilder.Build();
    }

    internal static string GetRequiredConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TrueMain");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing connection string. Add ConnectionStrings:TrueMain to user secrets.");
        }

        return connectionString;
    }
}
