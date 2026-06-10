using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Data.Logging;

/// <summary>
/// Registration helpers for the database logging sink so the API and the
/// Ingestor wire it up identically: bind <see cref="LoggingSinkOptions"/>,
/// share one <see cref="DatabaseLogChannel"/>, plug the
/// <see cref="DatabaseLoggerProvider"/> into the logging pipeline, and run the
/// draining <see cref="DatabaseLogSink"/>.
/// </summary>
public static class DatabaseLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the persistent database logging sink. Requires an
    /// <c>IDbContextFactory&lt;TrueMainDbContext&gt;</c> to already be (or to be
    /// later) registered — the API and the Ingestor both register one.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Root configuration to bind the
    /// <c>LoggingSink</c> section from.</param>
    /// <param name="processName">Optional host identifier stamped onto every
    /// record's <c>ProcessName</c> (e.g. "Api", "Ingestor"); overrides the
    /// configured value when provided.</param>
    public static IServiceCollection AddDatabaseLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        string? processName = null)
    {
        var optionsBuilder = services.AddOptions<LoggingSinkOptions>()
            .Bind(configuration.GetSection(LoggingSinkOptions.SectionName));

        if (processName is not null)
        {
            optionsBuilder.PostConfigure(options => options.ProcessName = processName);
        }

        // One channel shared by the provider (producer) and the sink (consumer).
        services.TryAddSingleton<DatabaseLogChannel>();

        // Add the provider to the logging pipeline. Registering the concrete
        // ILoggerProvider via the enumerable is how custom providers are plugged
        // in without disturbing the console/debug providers already present.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, DatabaseLoggerProvider>());

        services.AddHostedService<DatabaseLogSink>();

        return services;
    }
}
