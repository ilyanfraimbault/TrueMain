using Microsoft.Extensions.DependencyInjection.Extensions;
using TrueMain.Configuration;
using TrueMain.ReadModels.Ops;
using TrueMain.Options;

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
        services.AddMainAnalysisOptions();
        services.TryAddSingleton(typeof(Measurements<>));
        services.AddApiOptions<DataQualityDetectorOptions>();
        services.AddScoped<IIncompleteMatchesQueryService, IncompleteMatchesQueryService>();
        services.AddScoped<IMatchDataQualityDetailQueryService, MatchDataQualityDetailQueryService>();
        services.AddWarmCachedRead<IDataQualityDetectorsQueryService, DataQualityDetectorsQueryService, DataQualityDetectorsReadModel>(
            CachedDataQualityDetectorsQueryService.Payload,
            (inner, cache, clock) => new CachedDataQualityDetectorsQueryService(inner, cache, clock),
            inner => inner.GetDetectorsAsync(CancellationToken.None));
        return services;
    }
}
