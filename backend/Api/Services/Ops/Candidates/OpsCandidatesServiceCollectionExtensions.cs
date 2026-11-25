namespace TrueMain.Services.Ops.Candidates;

public static class OpsCandidatesServiceCollectionExtensions
{
    /// <summary>Registers the discovery candidates' list, funnel, stock and queue latency.</summary>
    public static IServiceCollection AddTrueMainOpsCandidates(this IServiceCollection services)
    {
        services.AddScoped<ICandidateQueryService, CandidateQueryService>();
        services.AddScoped<ICandidateFunnelQueryService, CandidateFunnelQueryService>();
        services.AddScoped<ICandidateStockQueryService, CandidateStockQueryService>();
        services.AddScoped<ICandidateQueueLatencyQueryService, CandidateQueueLatencyQueryService>();
        return services;
    }
}
