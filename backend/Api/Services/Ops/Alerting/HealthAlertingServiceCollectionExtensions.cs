using Microsoft.Extensions.Caching.Memory;
using TrueMain.Options;

namespace TrueMain.Services.Ops.Alerting;

public static class HealthAlertingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pipeline-health alerter (#2292): its options, the Discord channel, the
    /// hosted loop and the <c>/ops/health-alerts</c> read. Always registered — the loop returns
    /// at once when no webhook is configured, and the panel then says alerting is off. The
    /// store is registered by <c>AddMongoLogging</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainHealthAlerting(this IServiceCollection services)
    {
        services.AddOptions<AlertingOptions>()
            .BindConfiguration(AlertingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // No request loggers: they print the request URI, and the webhook's URI is its secret.
        services.AddHttpClient(DiscordHealthAlertNotifier.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .RemoveAllLoggers();
        services.AddSingleton<IHealthAlertNotifier, DiscordHealthAlertNotifier>();
        services.AddSingleton<IPipelineHealthSnapshotSource>(provider => new WarmPipelineHealthSnapshotSource(
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddHostedService<PipelineHealthAlerter>();
        services.AddScoped<IHealthAlertsQueryService, HealthAlertsQueryService>();
        return services;
    }
}
