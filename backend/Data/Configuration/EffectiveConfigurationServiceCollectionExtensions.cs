using Microsoft.Extensions.DependencyInjection;

namespace Data.Configuration;

/// <summary>
/// Registration helper for the boot-time effective-configuration publisher (#1034).
/// </summary>
public static class EffectiveConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Makes this host publish <paramref name="catalog"/>'s sections once at boot.
    ///
    /// <para>
    /// Register it before the host's own worker: hosted services start in registration order,
    /// so the snapshot is written before the pipeline's first pass — which matters for the
    /// ingestor, whose first pass can end the process.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Requires <c>AddMongoLogging</c> (for <c>IEffectiveConfigurationStore</c>) and a
    /// registered <see cref="TimeProvider"/>.
    /// </remarks>
    public static IServiceCollection AddEffectiveConfigurationPublisher(
        this IServiceCollection services,
        EffectiveConfigurationCatalog catalog)
    {
        services.AddSingleton(catalog);
        services.AddHostedService<EffectiveConfigurationPublisher>();

        return services;
    }
}
