using Microsoft.Extensions.Diagnostics.HealthChecks;
using TrueMain.Configuration;

namespace TrueMain.Services.Champions.Warmup;

public static class ChampionWarmupServiceCollectionExtensions
{
    /// <summary>
    /// The health check service, which logs every Degraded probe at Warning and every Unhealthy
    /// one at Error. With the warm-up on, the only Degraded check is the warm-up's own, held for
    /// the whole first pass and probed several times a minute: that category is kept to Error,
    /// so a failing check still logs on every probe and the hold is logged by
    /// <see cref="ChampionWarmupHealthCheck"/> once when it starts and once when it ends.
    /// </summary>
    internal const string HealthCheckServiceCategory =
        "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";

    /// <summary>
    /// Registers the warm-up's state whatever the configuration, since the read cache and the
    /// rate limiter ask it about every request; the warmer and the readiness check only when
    /// <see cref="ChampionWarmupOptions.Enabled"/>.
    /// </summary>
    public static IServiceCollection AddChampionWarmup(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiOptions<ChampionWarmupOptions>();
        services.AddSingleton<ChampionReadVersion>();

        if (configuration.GetSection(ChampionWarmupOptions.SectionName).GetValue<bool>(nameof(ChampionWarmupOptions.Enabled)))
        {
            services.AddHttpClient(ChampionCacheWarmer.ClientName);
            services.AddHostedService<ChampionCacheWarmer>();
            // A singleton, so the hold's start and end are logged once per process, not per probe.
            services.AddSingleton<ChampionWarmupHealthCheck>();
            services.AddLogging(logging => logging.AddFilter(HealthCheckServiceCategory, LogLevel.Error));
            services.AddHealthChecks().AddCheck<ChampionWarmupHealthCheck>(
                ChampionWarmupHealthCheck.Name,
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"]);
        }

        return services;
    }

    /// <summary>
    /// Puts a warm pass's own requests on its version. Ahead of the rate limiter, which lets
    /// them through: a pass is thousands of requests from one address, and every other request
    /// goes on being limited as usual.
    /// </summary>
    public static IApplicationBuilder UseChampionWarmupRequests(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            // In an async body so the ambient version ends with this request: a connection
            // reused by a visitor's next request must not inherit it.
            if (context.Request.Headers.TryGetValue(ChampionReadVersion.HeaderName, out var header))
            {
                context.RequestServices.GetRequiredService<ChampionReadVersion>().TryEnterPass(header);
            }

            await next(context);
        });
}
