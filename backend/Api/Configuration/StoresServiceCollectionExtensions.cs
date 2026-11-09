using Data;
using Data.Configuration;
using Data.Logging.Crash;
using Data.Logging.Mongo;

namespace TrueMain.Configuration;

public static class StoresServiceCollectionExtensions
{
    /// <summary>
    /// Postgres and Mongo as the Api uses them: the shared data source and contexts, the startup
    /// migrations, Warning+ logs and the audit writer in Mongo, and crash capture on top of it.
    /// </summary>
    public static IServiceCollection AddTrueMainStores(this IServiceCollection services, IConfiguration configuration)
    {
        // Read-only and idempotent, so the Api alone retries transient failures (#1634).
        services.AddTrueMainData(configuration, retryTransientFailures: true);
        services.AddOptionsSection<DatabaseOptions>(DatabaseOptions.SectionName, SharedEffectiveConfigurationSections.Database);
        // A hosted service (#258): the server only listens once it has started, so no request
        // reaches a stale schema. Gated on Database:ApplyMigrationsOnStartup, off in prod and preprod.
        services.AddDatabaseMigrationsOnStartup();
        services.AddMongoLogging(configuration, processName: "Api");
        services.AddSingleton(SharedEffectiveConfigurationSections.MongoLogging);
        services.AddCrashReporting();
        return services;
    }
}
