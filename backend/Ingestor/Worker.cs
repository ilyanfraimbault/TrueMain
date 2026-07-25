using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor;

public sealed class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> jobOptions,
    IIterationContext iterationContext,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    private const string HeartbeatEnvironmentVariable = "INGESTOR_HEARTBEAT_PATH";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = jobOptions.Value;
        var mode = JobModeParser.Parse(options.Mode);

        await ReconcileOrphanedRunsAsync(stoppingToken);

        do
        {
            TouchHeartbeat();
            await RunOnceAsync(mode, stoppingToken);

            if (options.RunOnce)
            {
                // A single scheduled run completed successfully; ask the host to
                // shut down so the process exits with a success code. Any failure
                // is left to propagate from ExecuteAsync so the host's exit code
                // reflects it (cooperative cancellation on shutdown is honoured by
                // the loop condition below).
                applicationLifetime.StopApplication();
                return;
            }

            var delayMinutes = options.IntervalMinutes is > 0 ? options.IntervalMinutes.Value : 60;
            logger.LogInformation(
                "Run completed. Waiting {DelayMinutes} minutes before next run.",
                delayMinutes);
            await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
    }

    private async Task ReconcileOrphanedRunsAsync(CancellationToken stoppingToken)
    {
        // Single-instance ingestor: any ProcessRun still Running at boot was
        // orphaned by the previous process (a crash, OOM-kill or redeploy) and can
        // never complete, so it would otherwise read as a ghost "Running" forever.
        // Reconcile once before the main loop. A failure here must never stop the
        // worker from starting, so it is caught and logged.
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IProcessRunRecorder>();
            var abandoned = await recorder.ReconcileOrphanedRunsAsync(stoppingToken);
            if (abandoned > 0)
            {
                logger.LogWarning(
                    "Reconciled {AbandonedCount} orphaned Running process run(s) to Abandoned at startup.",
                    abandoned);
            }
            else
            {
                logger.LogInformation("No orphaned Running process runs to reconcile at startup.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup reconciliation of orphaned process runs failed; continuing to start the worker.");
        }
    }

    private void TouchHeartbeat()
    {
        var path = Environment.GetEnvironmentVariable(HeartbeatEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            // The heartbeat is a liveness signal for the Docker healthcheck;
            // a write failure must not crash the worker. Log and move on so
            // the next iteration can retry — the healthcheck will mark the
            // container unhealthy if the file stays stale long enough.
            logger.LogWarning(ex, "Failed to update Ingestor heartbeat at {Path}.", path);
        }
    }

    private async Task RunOnceAsync(JobMode mode, CancellationToken stoppingToken)
    {
        try
        {
            await RunModeAsync(mode, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A single iteration failure must not kill the worker — long-running
            // ingestion services should self-heal across runs (transient DB / Riot
            // hiccups, schema drift caught by validation, etc.).
            logger.LogError(ex, "Ingestor run failed; will retry on next interval.");
        }
    }

    private async Task RunModeAsync(JobMode mode, CancellationToken stoppingToken)
    {
        var sequence = JobModeSequence.For(mode);

        // Open a fresh iteration for this whole pass so every ProcessRun recorded
        // below (across the per-process scopes) is stamped with the same id and the
        // admin can group them as one chain. The AsyncLocal id flows into each
        // awaited process; the scope restores the prior value when the pass ends.
        using var iteration = iterationContext.BeginIteration();
        logger.LogInformation("Starting iteration {IterationId}.", iteration.IterationId);

        foreach (var step in sequence)
        {
            // A fresh scope per process gives each one its own DbContext and
            // scoped repositories. A single shared scope would let the
            // ChangeTracker accumulate every entity touched across the whole
            // sequence and leak cached scoped state from one process into the
            // next. The scope is disposed before moving on to the next process.
            await using var scope = scopeFactory.CreateAsyncScope();

            // Resolve from THIS scope's provider (never the root one, see #256) so
            // the process and its scoped dependencies die with the scope.
            // GetRequiredKeyedService names only the key TYPE in its message, so
            // resolve leniently and throw one that names the offending mode. The
            // throw aborts this pass but is caught by RunOnceAsync, so a bad
            // registration is loud in the logs instead of crash-looping the host.
            var process = scope.ServiceProvider.GetKeyedService<IIngestorProcess>(step)
                ?? throw new InvalidOperationException(
                    $"No IIngestorProcess is registered for {nameof(JobMode)}.{step} "
                    + $"(reached while running Job:Mode '{mode}'). "
                    + $"Registered modes: {DescribeRegisteredModes(scope.ServiceProvider)}. "
                    + "Register the missing one via AddRecordedProcess<T>(JobMode) in AddIngestorProcesses.");

            try
            {
                await process.RunCoreAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One failing process must not starve the rest of the sequence: before
                // issue #443 a Discovery failure aborted every cycle here, so nothing
                // downstream (Scoring, MatchIngestion, ...) ever ran. The RecordedProcess
                // decorator has already persisted the Failed run for this process; log
                // and move on to the next one.
                logger.LogError(
                    ex,
                    "Process {ProcessName} failed; continuing with the next process in the sequence.",
                    process.Name);
            }
        }
    }

    private static string DescribeRegisteredModes(IServiceProvider serviceProvider)
    {
        // Keyed DI has no equivalent of the old name-indexed dictionary to read the
        // registrations off, but IServiceProviderIsKeyedService answers "is this key
        // registered?" without constructing anything. Enumerating
        // GetKeyedServices(AnyKey) instead would instantiate every process — each
        // with its DbContext and scoped dependencies — just to format an error
        // message, and could throw while doing so and mask the real fault.
        var probe = serviceProvider.GetService<IServiceProviderIsKeyedService>();
        if (probe is null)
        {
            return "unavailable (this container cannot be probed for keyed registrations)";
        }

        var registered = Enum.GetValues<JobMode>()
            .Where(candidate => probe.IsKeyedService(typeof(IIngestorProcess), candidate))
            .ToArray();

        return registered.Length == 0 ? "none" : string.Join(", ", registered);
    }
}
