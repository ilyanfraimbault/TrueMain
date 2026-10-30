using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services.Ops.Stats;

public static class OpsStatsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the query services behind <c>OpsStatsController</c>, the charted series under
    /// <c>/ops/stats</c> and <c>/ops/riot-*</c>: champion rows, matches over time, matches
    /// ingested, aggregation progress, Riot API usage and quota, and the Ingestor's own meter
    /// (#1636). The Mongo stores they read are registered by <c>AddMongoLogging</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainOpsStats(this IServiceCollection services)
    {
        services.AddScoped<IChampionStatsQueryService, ChampionStatsQueryService>();
        services.AddScoped<IMatchesOverTimeQueryService, MatchesOverTimeQueryService>();
        services.AddScoped<IMatchesIngestedQueryService, MatchesIngestedQueryService>();
        // Callers get the cached, single-flighted payload; only the decorator reaches the
        // measurement itself (#2303).
        services.AddScoped<AggregationStatsQueryService>();
        services.AddScoped<IAggregationStatsQueryService>(provider => new CachedAggregationStatsQueryService(
            provider.GetRequiredService<AggregationStatsQueryService>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddWarmPayload(
            CachedAggregationStatsQueryService.Payload,
            provider => provider.GetRequiredService<AggregationStatsQueryService>().GetAsync(CancellationToken.None));
        services.AddScoped<IRiotApiUsageQueryService, RiotApiUsageQueryService>();
        services.AddScoped<IRiotQuotaQueryService, RiotQuotaQueryService>();
        services.AddScoped<IIngestorMetricsQueryService, IngestorMetricsQueryService>();
        return services;
    }
}
