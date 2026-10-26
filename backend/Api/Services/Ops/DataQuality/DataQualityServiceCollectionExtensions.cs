using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services.Ops.DataQuality;

public static class DataQualityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the query services behind <c>OpsDataQualityController</c>. The detector suite
    /// is registered behind its caching decorator and kept warm in the background (#2305);
    /// only the decorator and the refresher reach the detectors themselves.
    /// </summary>
    public static IServiceCollection AddTrueMainDataQuality(this IServiceCollection services)
    {
        services.AddScoped<IIncompleteMatchesQueryService, IncompleteMatchesQueryService>();
        services.AddScoped<IMatchDataQualityDetailQueryService, MatchDataQualityDetailQueryService>();
        services.AddScoped<DataQualityDetectorsQueryService>();
        services.AddScoped<IDataQualityDetectorsQueryService>(provider => new CachedDataQualityDetectorsQueryService(
            provider.GetRequiredService<DataQualityDetectorsQueryService>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddWarmPayload(
            CachedDataQualityDetectorsQueryService.Payload,
            provider => provider.GetRequiredService<DataQualityDetectorsQueryService>()
                .GetDetectorsAsync(CancellationToken.None));
        return services;
    }
}
