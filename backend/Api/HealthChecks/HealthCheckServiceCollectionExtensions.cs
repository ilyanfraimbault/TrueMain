namespace TrueMain.HealthChecks;

public static class HealthCheckServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>/readyz</c>'s Postgres probe, through the shared <c>NpgsqlDataSource</c>
    /// <c>AddTrueMainData</c> builds: the connection-string overload would open a second pool
    /// outside the connection budget (#1637).
    /// </summary>
    public static IServiceCollection AddTrueMainHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var healthChecks = services.AddHealthChecks();
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("TrueMain")))
        {
            healthChecks.AddNpgSql(
                serviceProvider => serviceProvider.GetRequiredService<Npgsql.NpgsqlDataSource>(),
                name: "postgres",
                tags: ["ready"]);
        }
        else if (environment.IsProduction())
        {
            // Without it /readyz would stay green while the app cannot reach Postgres. Development
            // boots before user secrets are wired, and the integration-test "Testing" host injects
            // the connection string after this point (ConfigureAppConfiguration).
            throw new InvalidOperationException(
                "Missing connection string. Add ConnectionStrings:TrueMain so the Postgres "
                + "readiness health check can be registered in Production.");
        }

        return services;
    }
}
