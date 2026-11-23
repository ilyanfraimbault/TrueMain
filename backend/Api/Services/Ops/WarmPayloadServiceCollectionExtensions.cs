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

    /// <summary>
    /// A read callers get through its caching decorator, kept warm in the background: registers
    /// <typeparamref name="TInner"/> (the computation), <typeparamref name="TService"/> as the
    /// decorator <paramref name="decorate"/> builds over it, and <paramref name="payload"/> for the
    /// refresher, which recomputes through <paramref name="compute"/> on the uncached
    /// <typeparamref name="TInner"/>.
    /// </summary>
    public static IServiceCollection AddWarmCachedRead<TService, TInner, T>(
        this IServiceCollection services,
        WarmPayload<T> payload,
        Func<TInner, IMemoryCache, TimeProvider, TService> decorate,
        Func<TInner, Task<T>> compute)
        where TService : class
        where TInner : class, TService
        where T : class
    {
        services.AddScoped<TInner>();
        services.AddScoped(provider => decorate(
            provider.GetRequiredService<TInner>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        return services.AddWarmPayload(payload, provider => compute(provider.GetRequiredService<TInner>()));
    }
}
