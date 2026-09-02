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
    ICallerContext callerContext,
    IHostApplicationLifetime applicationLifetime,
    IngestorMetrics metrics,
    TimeProvider timeProvider,
    IHeartbeatFile heartbeatFile) : BackgroundService
{

    /// <summary>
    /// How often the liveness file is rewritten. Same cadence as
    /// <see cref="Processes.RecordedProcess{TInner}"/>'s <c>process_runs</c> heartbeat, for
    /// the same reason: a signal that only ticks at pass boundaries cannot distinguish a
    /// wedged process from a slow one (#1229).
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = jobOptions.Value;
        var mode = JobModeParser.Parse(options.Mode);

        await ReconcileOrphanedRunsAsync(mode, stoppingToken);

        // The heartbeat used to be touched once per iteration, at the top. A Full pass runs
        // for many minutes and the wait between passes for a whole Job:IntervalMinutes, so
        // the healthcheck had to tolerate 6 h of silence to avoid killing a container that
        // was working normally — which left it unable to detect anything short of a process
        // that had been dead for a quarter of a day. Refreshing on its own loop, for as long
        // as the worker lives, restores what a container liveness probe is supposed to
        // assert: the process is up and its scheduling still runs. Whether the *work* is
        // progressing is a separate question, and it already has a separate answer — the
        // per-run heartbeat on process_runs, which ages a stalled run out to Abandoned.
        await TouchHeartbeatAsync(stoppingToken);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatLoop = RunHeartbeatLoopAsync(heartbeatCts.Token);

        try
        {
            do
            {
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

                // Non-null and > 0 here: startup validation (Job:IntervalMinutes) rejects anything
                // else whenever RunOnce is false, which is the only way execution reaches this line.
                var delayMinutes = options.IntervalMinutes!.Value;
                logger.LogInformation(
                    "Run completed. Waiting {DelayMinutes} minutes before next run.",
                    delayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
            } while (!stoppingToken.IsCancellationRequested);
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            // The loop never throws (it catches everything internally), so this just
            // joins it before the worker returns.
            await heartbeatLoop;
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Through TimeProvider, not Task.Delay: a 30 s wall-clock beat is
                // otherwise untestable by construction, and the property worth pinning
                // is precisely that this cadence is independent of how long a pass
                // takes. Only this loop needs it — the between-passes wait below is
                // driven by configuration a test sets directly.
                await Task.Delay(HeartbeatInterval, timeProvider, ct);
                await TouchHeartbeatAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: shutdown, or the finally above joining the loop.
        }
    }

    private async Task ReconcileOrphanedRunsAsync(JobMode mode, CancellationToken stoppingToken)
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

            // Only this instance's own steps (#1362). Running the pipeline as two lanes puts
            // two ingestor processes on the same database, and a sweep that abandoned every
            // Running document would mark the other lane's live run as dead on every restart.
            //
            // Resolved with GetKeyedService, not GetRequiredKeyedService: a step with no
            // registration is a wiring mistake that the run itself reports, precisely and by
            // name. Throwing here would bury that behind a second, vaguer boot error.
            var ownedProcesses = JobModeSequence.For(mode)
                .Select(step => scope.ServiceProvider.GetKeyedService<IIngestorProcess>(step)?.Name)
                .OfType<string>()
                .ToList();

            if (ownedProcesses.Count == 0)
            {
                // An empty list means "every process" to the store, which is the one thing
                // this must not do when it does not know what it owns.
                logger.LogWarning(
                    "No registered process resolved for mode {Mode}; skipping orphaned-run reconciliation.",
                    mode);
                return;
            }

            var abandoned = await recorder.ReconcileOrphanedRunsAsync(ownedProcesses, stoppingToken);
            if (abandoned > 0)
            {
                logger.LogWarning(
                    "Reconciled {AbandonedCount} orphaned Running process run(s) of mode {Mode} to Abandoned at startup.",
                    abandoned,
                    mode);
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

    private async Task TouchHeartbeatAsync(CancellationToken ct)
    {
        // Resolved once, by the injected file, rather than read from the environment on
        // every beat: the environment is process-global and a worker is not (#1348).
        var path = heartbeatFile.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O"), ct);
        }
        // A cancelled write is shutdown, not a heartbeat failure: let it propagate
        // so it is handled where every other OperationCanceledException in this
        // worker is, instead of being logged as a warning on the way out. Matched
        // on the exception type, not merely on the token's state — otherwise a real
        // I/O failure racing a shutdown would escape the catch below and take the
        // host down, which is exactly what that block exists to prevent.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The heartbeat is a liveness signal for the Docker healthcheck;
            // a write failure must not crash the worker. Log and move on so
            // the next beat can retry — the healthcheck will mark the
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
            // A cooperative shutdown is not a failure (#255): it keeps propagating
            // untouched and must never reach the failure counter, otherwise every
            // redeploy would look like a broken pipeline.
            throw;
        }
        catch (Exception ex)
        {
            // A single iteration failure must not kill the worker — long-running
            // ingestion services should self-heal across runs (transient DB / Riot
            // hiccups, schema drift caught by validation, etc.).
            logger.LogError(ex, "Ingestor run failed; will retry on next interval.");

            // ...but a log line alone is not alertable, so the failure is also
            // published as a counter (#260). Failures escaping this far are not
            // attributable to one process (those are counted in RunModeAsync).
            metrics.RecordRunFailure(IngestorMetrics.WholeRunProcess, mode);
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

            // Ambient caller attribution (#1035): every Riot call the process makes
            // while this scope is open is tagged with its name in the usage rollups,
            // so /riot-api can attribute consumption per caller. Scoped to just the
            // RunCoreAsync call so it never leaks into the next process below.
            using var callerScope = callerContext.BeginCall(process.Name);

            try
            {
                await process.RunCoreAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown, not a failure (#255) — propagate without counting it.
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

                // This is the catch that hides the failures worth alerting on: a dead
                // Riot key or schema drift surfaces here, per process, and never reaches
                // RunOnceAsync. Counting only the outer catch would leave the counter at
                // zero in exactly the scenario it exists for (#260).
                metrics.RecordRunFailure(process.Name, mode);
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
