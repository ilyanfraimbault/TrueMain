using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TrueMain.Services.Champions.Warmup;

public static class ChampionWarmupServiceCollectionExtensions
{
    /// <summary>
    /// Registers the warm-up's state whatever the configuration, since the read cache and the
    /// rate limiter ask it about every request; the warmer and the readiness check only when
    /// <see cref="ChampionWarmupOptions.Enabled"/>.
    /// </summary>
    public static IServiceCollection AddChampionWarmup(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ChampionWarmupOptions.SectionName);
        services.AddOptions<ChampionWarmupOptions>()
            .Bind(section)
            .Validate(options => options.Concurrency > 0, "ChampionWarmup:Concurrency must be greater than 0.")
            .Validate(options => options.StampPollSeconds > 0, "ChampionWarmup:StampPollSeconds must be greater than 0.")
            .Validate(options => options.PatchDepth > 0, "ChampionWarmup:PatchDepth must be greater than 0.")
            .ValidateOnStart();
        services.AddSingleton<ChampionReadVersion>();

        if (section.GetValue<bool>(nameof(ChampionWarmupOptions.Enabled)))
        {
            services.AddHttpClient(ChampionCacheWarmer.ClientName);
            services.AddHostedService<ChampionCacheWarmer>();
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
