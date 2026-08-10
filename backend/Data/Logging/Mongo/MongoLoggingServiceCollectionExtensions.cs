using Data.Metrics.Mongo;
using Data.Ops.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// Registration helpers for the MongoDB-backed observability stores so the API
/// and the Ingestor wire them up identically: bind <see cref="MongoLoggingOptions"/>,
/// share one <see cref="MongoLogContext"/> + <see cref="MongoLogChannel"/>, plug
/// the <see cref="MongoLoggerProvider"/> into the logging pipeline, run the
/// draining <see cref="MongoLogSink"/>, expose the lossless <see cref="IAuditLog"/>,
/// and wire the Riot API usage metrics store (#93): its channel, recorder, draining
/// <see cref="RiotApiMetricsSink"/> and the <see cref="IRiotApiUsageQuery"/> read.
/// </summary>
public static class MongoLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the MongoDB diagnostic-log sink and the operator-action audit writer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Root configuration to bind the
    /// <c>MongoLogging</c> section from.</param>
    /// <param name="processName">Optional host identifier stamped onto every
    /// diagnostic record's <c>processName</c> (e.g. "Api", "Ingestor"); overrides
    /// the configured value when provided.</param>
    public static IServiceCollection AddMongoLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        string? processName = null)
    {
        var optionsBuilder = services.AddOptions<MongoLoggingOptions>()
            .Bind(configuration.GetSection(MongoLoggingOptions.SectionName));

        if (processName is not null)
        {
            optionsBuilder.PostConfigure(options => options.ProcessName = processName);
        }

        // One client/database holder and one channel shared by the provider
        // (producer) and the sink (consumer).
        services.TryAddSingleton<MongoLogContext>();
        services.TryAddSingleton<MongoLogChannel>();

        // The lossless audit writer (synchronous insert, never the channel). The
        // read query for /ops/logs. Both reuse the singleton MongoLogContext.
        services.TryAddSingleton<IAuditLog, MongoAuditLog>();
        services.TryAddSingleton<IMongoLogQuery, MongoLogQuery>();

        // Add the provider to the logging pipeline. Registering the concrete
        // ILoggerProvider via the enumerable plugs a custom provider in without
        // disturbing the console/debug providers already present.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, MongoLoggerProvider>());

        services.AddHostedService<MongoLogSink>();

        // Riot API usage metrics (#93): a separate non-blocking channel + draining
        // sink folding calls into per-minute rollups in riot_api_call_rollups, the
        // recorder the Ingestor's HTTP handler calls, and the read query for
        // /ops/riot-usage.
        // All reuse the singleton MongoLogContext (same Mongo client/database).
        // Registered in both hosts like the log sink: the Ingestor produces the
        // records, the Api only reads, but a host with no producers just idles.
        services.TryAddSingleton<RiotApiMetricsChannel>();
        services.TryAddSingleton<IRiotApiCallRecorder, RiotApiCallRecorder>();
        services.TryAddSingleton<IRiotApiUsageQuery, RiotApiUsageQuery>();
        services.AddHostedService<RiotApiMetricsSink>();

        // Daily storage snapshots (#925). No sink and no channel: the Ingestor's
        // snapshot step calls the store directly once per pipeline run, and the Api
        // only reads it for the admin growth charts. Since #1023 the snapshot also
        // measures Mongo itself, through the stats reader — the Api uses it too, for
        // the live sizes panel.
        services.TryAddSingleton<IDbStorageSnapshotStore, DbStorageSnapshotStore>();
        services.TryAddSingleton<IMongoStorageStatsReader, MongoStorageStatsReader>();

        // Admin-portal data moved off Postgres: recorded process runs (written by
        // the Ingestor's ProcessRunRecorder, read by the admin process panels) and
        // the seed-request queue (written by the API, claimed by ManualSeedProcess).
        // Direct-call stores like the snapshots — no sink, no channel.
        services.TryAddSingleton<IProcessRunStore, ProcessRunStore>();
        services.TryAddSingleton<ISeedRequestStore, SeedRequestStore>();

        // Effective-configuration snapshots (#1034): written by the Ingestor's boot-time
        // publisher, read by the Api for the admin configuration page. Registered in both hosts
        // like the stores above — the Api never writes, the Ingestor never reads.
        services.TryAddSingleton<IEffectiveConfigurationStore, EffectiveConfigurationStore>();

        return services;
    }
}
