using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Configuration;

/// <summary>
/// Appends this process's boot — version and start time — to the boot log once, at startup
/// (#2297), which is what the admin charts' deploy markers are read from.
///
/// <para>
/// An <see cref="IHostedService"/> for the reason <see cref="EffectiveConfigurationPublisher"/>
/// is one: the ingestor's <c>RunOnce</c> worker can end the process after its first pass, and a
/// background task would race that shutdown. Bounded and fail-soft like it too — the boot log is
/// observability, and Mongo being down must not hold a pipeline's boot.
/// </para>
/// </summary>
public sealed class ProcessBootRecorder(
    IProcessBootStore store,
    IOptions<MongoLoggingOptions> mongoOptions,
    IConfiguration configuration,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ProcessBootRole role,
    ILogger<ProcessBootRecorder> logger) : IHostedService
{
    private static readonly TimeSpan RecordTimeout = TimeSpan.FromSeconds(10);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var processName = mongoOptions.Value.ProcessName ?? environment.ApplicationName;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RecordTimeout);

            var boot = new ProcessBoot(
                processName,
                role.Value,
                environment.EnvironmentName,
                AppVersion.FromConfiguration(configuration),
                timeProvider.GetUtcNow().UtcDateTime);

            var deployed = await store.RecordAsync(boot, timeout.Token);
            if (deployed == true)
            {
                logger.LogInformation(
                    "{ProcessName} booted on {Version}, a new version: recorded as a deploy.",
                    processName,
                    boot.Version);
            }
        }
#pragma warning disable CA1031 // Observability must never take the pipeline down with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Could not record the boot of {ProcessName}. The admin charts will miss this deploy marker if it was one.",
                processName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// What this container of the process does, when one image runs as several (the ingestor's
/// <c>Job:Mode</c>); null for a process that runs once.
/// </summary>
public sealed record ProcessBootRole(string? Value);
