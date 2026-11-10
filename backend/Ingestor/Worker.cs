using Data.Ops.Mongo;
using Data.Pipeline;
using Ingestor.FetchWorkers;
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
    ProcessRunner runner,
    IHostApplicationLifetime applicationLifetime,
    IngestorMetrics metrics,
    TimeProvider timeProvider,
    IHeartbeatFile heartbeatFile) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = jobOptions.Value;
        var mode = JobModeParser.Parse(options.Mode);

        await ReconcileOrphanedRunsAsync(mode, stoppingToken);
        await RecoverOwnedStepsAsync(mode, stoppingToken);

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

        // FetchWorkers (#1457): match fetching runs beside the loop below, as permanent
        // workers, rather than as one of its steps. Started before the first maintenance pass
        // and on the host's token, so they stop when the host does, never when a pass ends.
        var fetchWorkers = RunFetchWorkersAsync(mode, options.RunOnce, stoppingToken);

        try
        {
            do
            {
                await RunOnceAsync(mode, stoppingToken);

                if (options.RunOnce)
                {
                    // RunOnce is one pass of everything the lane owns: for FetchWorkers that
                    // is one batch per worker too, so the exit waits for them.
                    await fetchWorkers;

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
            // The workers stop on the host's token; this joins them, so their cancelled
            // batches have released their claims before the host goes.
            await fetchWorkers;
            await heartbeatCts.CancelAsync();
            // The loop never throws (it catches everything internally), so this just
            // joins it before the worker returns.
            await heartbeatLoop;
        }
    }

    private async Task RunFetchWorkersAsync(JobMode mode, bool runOnce, CancellationToken stoppingToken)
    {
        if (!PipelineCatalog.RunsPermanentFetchWorkers(mode))
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MatchFetchWorkerPool>().RunAsync(runOnce, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: the workers have released their claims on the way out.
        }
        catch (Exception ex)
        {
            // Each worker catches its own batch failures, so reaching here is a wiring fault
            // (no pool registered, no process for MatchIngestionOnly). Logged and counted like
            // a failed pass; the maintenance lane keeps running rather than taking the host down.
            logger.LogError(ex, "The match-fetch workers stopped on an unexpected failure.");
            metrics.RecordRunFailure(IngestorMetrics.WholeRunProcess, mode);
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
                await Task.Delay(ProcessRunStaleness.HeartbeatInterval, timeProvider, ct);
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
            var ownedProcesses = PipelineCatalog.StepsOwnedBy(mode).Select(step => step.Name).ToList();

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

    private async Task RecoverOwnedStepsAsync(JobMode mode, CancellationToken stoppingToken)
    {
        // Only the steps this lane owns (#1513): the other lane's claims sit in the same tables
        // and are live right now. A single-instance lane cannot hold a live claim at boot, so
        // whatever its own steps still hold was stranded by the incarnation that died.
        var owned = PipelineCatalog.StepsOwnedBy(mode);

        await using var scope = scopeFactory.CreateAsyncScope();
        IEnumerable<IStepBootRecovery> recoveries;
        try
        {
            recoveries = scope.ServiceProvider.GetServices<IStepBootRecovery>().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup recovery could not be set up; continuing to start the worker.");
            return;
        }

        foreach (var recovery in recoveries.Where(recovery => owned.Contains(recovery.Step)))
        {
            try
            {
                await recovery.RecoverAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Never block the boot: each step's own fallback (the claim lease, the reap at
                // the top of its next run) still covers what this leaves behind.
                logger.LogError(
                    ex,
                    "Startup recovery of {ProcessName} failed; continuing to start the worker.",
                    recovery.Step.Name);
            }
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
            await File.WriteAllTextAsync(path, timeProvider.GetUtcNow().ToString("O"), ct);
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
            // attributable to one process (those are counted by the ProcessRunner).
            metrics.RecordRunFailure(IngestorMetrics.WholeRunProcess, mode);
        }
    }

    private async Task RunModeAsync(JobMode mode, CancellationToken stoppingToken)
    {
        // Less the steps permanent workers run on their own (#1457), so a FetchWorkers pass
        // is the fetch lane's maintenance: every step but match fetching.
        var sequence = PipelineCatalog.SequencedStepsFor(mode);

        // Open a fresh iteration for this whole pass so every ProcessRun recorded
        // below (across the per-process scopes) is stamped with the same id and the
        // admin can group them as one chain. The AsyncLocal id flows into each
        // awaited process; the scope restores the prior value when the pass ends.
        using var iteration = iterationContext.BeginIteration(mode);
        logger.LogInformation("Starting iteration {IterationId}.", iteration.IterationId);

        foreach (var step in sequence)
        {
            // A failed step is logged and counted by the runner and the pass moves on: one
            // failing process must not starve the rest of the sequence (#443).
            await runner.RunAsync(step, mode, RunInput.Whole, stoppingToken);
        }
    }
}
