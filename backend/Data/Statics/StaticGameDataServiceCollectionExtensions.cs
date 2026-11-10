using Data.BuildFacts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Registers the four static game data providers — item metadata and shop classes from
/// CommunityDragon, champion statics and item stats from Data Dragon — for every host that reads
/// them, so the Api and the ingestor cannot wire them differently.
/// </summary>
public static class StaticGameDataServiceCollectionExtensions
{
    /// <summary>The named HTTP client every provider loads through; each host configures its pipeline.</summary>
    public const string ClientName = "StaticGameData";

    /// <summary>
    /// Every provider is a singleton: its per-patch cache, and the clock of how long a patch-day
    /// fallback has stood, outlive a request or a fold run (<see cref="PerPatchStaticCache{T}"/>).
    /// Each load takes a fresh client from <paramref name="createClient"/> (the host's
    /// <c>IHttpClientFactory</c>), so no client outlives its handler's rotation.
    /// </summary>
    public static IServiceCollection AddStaticGameData(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient> createClient)
    {
        services.AddSingleton<IItemMetadataProvider>(provider => new CommunityDragonItemMetadataProvider(
            () => createClient(provider),
            provider.GetRequiredService<ILogger<CommunityDragonItemMetadataProvider>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IItemClassProvider>(provider => new CommunityDragonItemClassProvider(
            () => createClient(provider),
            provider.GetRequiredService<ILogger<CommunityDragonItemClassProvider>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IChampionStaticsProvider>(provider => new DataDragonChampionStaticsProvider(
            () => createClient(provider),
            provider.GetRequiredService<ILogger<DataDragonChampionStaticsProvider>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IItemStatsProvider>(provider => new DataDragonItemStatsProvider(
            () => createClient(provider),
            provider.GetRequiredService<ILogger<DataDragonItemStatsProvider>>(),
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
