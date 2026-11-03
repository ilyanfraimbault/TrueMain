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

    /// <summary>
    /// Makes this host append its boot (version, start time) to the boot log once at startup
    /// (#2297). <paramref name="role"/> names which container of a multi-container process this
    /// is (the ingestor's job mode); omit it for a process that runs once.
    /// </summary>
    /// <remarks>
    /// Same requirements and the same ordering rule as
    /// <see cref="AddEffectiveConfigurationPublisher"/>: register it before the host's worker.
    /// </remarks>
    public static IServiceCollection AddProcessBootRecorder(
        this IServiceCollection services,
        Func<IServiceProvider, string?>? role = null)
    {
        services.AddSingleton(provider => new ProcessBootRole(role?.Invoke(provider)));
        services.AddHostedService<ProcessBootRecorder>();

        return services;
    }
}
