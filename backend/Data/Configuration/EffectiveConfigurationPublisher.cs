using Data.Ops.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Data.Configuration;

/// <summary>
/// Publishes this process's effective configuration once, at boot (#1034), so the admin portal
/// can show what a host it cannot introspect is actually running with.
///
/// <para>
/// <b>An <see cref="IHostedService"/> rather than a <c>BackgroundService</c>.</b> The ingestor
/// runs with <c>Job:RunOnce</c> by default, and its worker calls <c>StopApplication</c> as soon
/// as one pass finishes — a background task would be racing that shutdown and, on a fast pass,
/// would lose. <c>StartAsync</c> also runs after <c>IStartupValidator</c>, so every
/// <c>ValidateOnStart</c> predicate has already passed and the values being published are the
/// validated ones.
/// </para>
/// </summary>
public sealed class EffectiveConfigurationPublisher(
    EffectiveConfigurationCatalog catalog,
    IServiceProvider services,
    IConfiguration configuration,
    IHostEnvironment environment,
    IEffectiveConfigurationStore store,
    TimeProvider timeProvider,
    ILogger<EffectiveConfigurationPublisher> logger) : IHostedService
{
    /// <summary>
    /// Upper bound on the publish. Mongo is optional everywhere and the client connects lazily,
    /// so a wedged server would otherwise stall the boot of a pipeline that does not need it.
    /// </summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PublishTimeout);

            var snapshot = EffectiveConfigurationBuilder.Build(
                catalog,
                services,
                configuration,
                environment.EnvironmentName,
                timeProvider.GetUtcNow().UtcDateTime);

            var published = await store.UpsertAsync(snapshot, timeout.Token);

            if (published)
            {
                logger.LogDebug(
                    "Published the effective configuration for {ProcessName} ({SectionCount} sections).",
                    snapshot.ProcessName,
                    snapshot.Sections.Count);
            }
        }
#pragma warning disable CA1031 // Observability must never take the pipeline down with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Could not publish the effective configuration for {ProcessName}. The admin "
                + "configuration page will show a stale snapshot for this process, or none.",
                catalog.ProcessName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
