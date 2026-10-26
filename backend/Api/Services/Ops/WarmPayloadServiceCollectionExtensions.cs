using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TrueMain.Services.Ops;

internal static class WarmPayloadServiceCollectionExtensions
{
    /// <summary>
    /// Hands <paramref name="payload"/> to the <see cref="CockpitCacheRefresher"/> (#2305),
    /// registered on the first call. <paramref name="compute"/> runs in a scope the refresher
    /// creates, so it resolves the <em>uncached</em> service — the caching decorator would
    /// only answer from the cache it is meant to refill.
    /// </summary>
    public static IServiceCollection AddWarmPayload<T>(
        this IServiceCollection services,
        WarmPayload<T> payload,
        Func<IServiceProvider, Task<T>> compute)
        where T : class
    {
        services.AddSingleton(new WarmPayloadTarget(
            payload,
            provider => payload.RefreshAsync(
                provider.GetRequiredService<IMemoryCache>(),
                () => compute(provider),
                provider.GetRequiredService<TimeProvider>())));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CockpitCacheRefresher>());
        return services;
    }
}
