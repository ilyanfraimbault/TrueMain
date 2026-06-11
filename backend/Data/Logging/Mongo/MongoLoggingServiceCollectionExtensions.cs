using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// Registration helpers for the MongoDB-backed logging store so the API and the
/// Ingestor wire it up identically: bind <see cref="MongoLoggingOptions"/>, share
/// one <see cref="MongoLogContext"/> + <see cref="MongoLogChannel"/>, plug the
/// <see cref="MongoLoggerProvider"/> into the logging pipeline, run the draining
/// <see cref="MongoLogSink"/>, and expose the lossless <see cref="IAuditLog"/>.
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

        return services;
    }
}
