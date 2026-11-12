using Microsoft.Extensions.DependencyInjection.Extensions;
using TrueMain.Configuration;
using TrueMain.ReadModels.Ops;
using TrueMain.Options;

namespace TrueMain.Services.Ops.Coverage;

public static class PatchCoverageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the query service behind <c>GET /ops/patch-coverage</c> (#1033). Callers get
    /// the cached, single-flighted payload; only the decorator reaches the evaluation itself
    /// (#2302), kept warm in the background while someone reads it (#2305).
    /// </summary>
    public static IServiceCollection AddTrueMainPatchCoverage(this IServiceCollection services)
    {
        services.AddMainAnalysisOptions();
        services.TryAddSingleton(typeof(Measurements<>));
        services.AddApiOptions<ChampionsListOptions>();
        services.AddApiOptions<DataQualityDetectorOptions>();
        services.AddApiOptions<PatchCoverageOptions>();
        services.AddWarmCachedRead<IPatchCoverageQueryService, PatchCoverageQueryService, PatchCoverageReadModel>(
            CachedPatchCoverageQueryService.Payload,
            (inner, cache, clock) => new CachedPatchCoverageQueryService(inner, cache, clock),
            inner => inner.GetAsync(CancellationToken.None));
        return services;
    }
}
