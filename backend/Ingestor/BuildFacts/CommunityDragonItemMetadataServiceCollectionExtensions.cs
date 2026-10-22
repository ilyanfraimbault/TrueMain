using Data.BuildFacts;

namespace Ingestor.BuildFacts;

/// <summary>
/// Registers the ingestor's item-metadata provider on its named CommunityDragon client.
/// </summary>
public static class CommunityDragonItemMetadataServiceCollectionExtensions
{
    /// <summary>
    /// One provider per resolution, so its per-patch cache lives as long as the process run that
    /// holds it (the Api keeps a singleton instead, #2212); each load takes a fresh client from
    /// the factory.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureClient">Configures the named client, as a typed client's delegate would.</param>
    /// <returns>The named client's builder, so the resilience pipeline can be chained.</returns>
    public static IHttpClientBuilder AddCommunityDragonItemMetadata(
        this IServiceCollection services,
        Action<IServiceProvider, HttpClient> configureClient)
    {
        services.AddTransient<IItemMetadataProvider>(serviceProvider => new CommunityDragonItemMetadataProvider(
            () => serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CommunityDragonItemMetadataProvider)),
            serviceProvider.GetRequiredService<ILogger<CommunityDragonItemMetadataProvider>>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        return services.AddHttpClient(nameof(CommunityDragonItemMetadataProvider), configureClient);
    }
}
