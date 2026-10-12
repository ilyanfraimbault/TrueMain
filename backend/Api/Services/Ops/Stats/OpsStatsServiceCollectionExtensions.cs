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
        services.AddScoped<IAggregationStatsQueryService, AggregationStatsQueryService>();
        services.AddScoped<IRiotApiUsageQueryService, RiotApiUsageQueryService>();
        services.AddScoped<IRiotQuotaQueryService, RiotQuotaQueryService>();
        services.AddScoped<IIngestorMetricsQueryService, IngestorMetricsQueryService>();
        return services;
    }
}
