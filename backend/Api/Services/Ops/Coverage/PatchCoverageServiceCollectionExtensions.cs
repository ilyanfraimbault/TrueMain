using Microsoft.Extensions.Caching.Memory;

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
        services.AddScoped<PatchCoverageQueryService>();
        services.AddScoped<IPatchCoverageQueryService>(provider => new CachedPatchCoverageQueryService(
            provider.GetRequiredService<PatchCoverageQueryService>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddWarmPayload(
            CachedPatchCoverageQueryService.Payload,
            provider => provider.GetRequiredService<PatchCoverageQueryService>().GetAsync(CancellationToken.None));
        return services;
    }
}
