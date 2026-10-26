using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services.Ops.Health;

public static class OpsHealthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the whole-system reads the admin portal opens on (<c>OpsOverviewController</c>)
    /// and the region-balance panel beside the cockpit.
    ///
    /// <para>
    /// The cockpit and the Overview counters are both registered behind their caching
    /// decorator: the cockpit is re-asked every 30 s by every open admin tab (#1411), the
    /// Overview on every visit, so callers get the cached, single-flighted payload and only
    /// the decorator reaches the computation itself (#1427, #2301). Both are kept warm in the
    /// background while someone reads them (#2305).
    /// </para>
    /// </summary>
    public static IServiceCollection AddTrueMainOpsHealth(this IServiceCollection services)
    {
        services.AddScoped<PipelineHealthQueryService>();
        services.AddScoped<IPipelineHealthQueryService>(provider => new CachedPipelineHealthQueryService(
            provider.GetRequiredService<PipelineHealthQueryService>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddWarmPayload(
            CachedPipelineHealthQueryService.Payload,
            provider => provider.GetRequiredService<PipelineHealthQueryService>().GetAsync(CancellationToken.None));
        services.AddScoped<IRegionBalanceQueryService, RegionBalanceQueryService>();
        services.AddScoped<OverviewQueryService>();
        services.AddScoped<IOverviewQueryService>(provider => new CachedOverviewQueryService(
            provider.GetRequiredService<OverviewQueryService>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddWarmPayload(
            CachedOverviewQueryService.Payload,
            provider => provider.GetRequiredService<OverviewQueryService>().GetAsync(CancellationToken.None));
        return services;
    }
}
