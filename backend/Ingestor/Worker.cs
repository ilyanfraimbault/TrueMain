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
        var sequence = mode switch
        {
            JobMode.DiscoveryOnly => ["Discovery"],
            JobMode.ManualSeedOnly => ["ManualSeed"],
            JobMode.HarvestOnly => ["Harvest"],
            JobMode.ScoringOnly => ["Scoring"],
            JobMode.MatchIngestionOnly => ["MatchIngestion"],
            JobMode.MainAnalysisOnly => ["MainAnalysis"],
            JobMode.PatternAggregationOnly => ["ChampionPatternAggregation"],
            JobMode.MatchupLeadAggregationOnly => ["ChampionMatchupLeadAggregation"],
            JobMode.AccountRefreshOnly => ["AccountRefresh"],
            JobMode.MatchDataRetentionOnly => ["MatchDataRetention"],
            _ => (string[])
            [
                "Discovery",
                // ManualSeed runs right after Discovery and before Scoring: it
                // queues its candidates directly (skipping the competitive top-N
                // ScoringProcess), so a seeded account is picked up by the same
                // downstream MatchIngestion -> MainAnalysis pass in this run.
                "ManualSeed",
                // Harvest generates candidates from orphan match_participants rows at
                // near-zero Riot cost (#485). It runs before Scoring so harvested
                // candidates compete in the same per-platform top-N as ladder/manual ones.
                "Harvest",
                "Scoring",
                "MatchIngestion",
                "MainAnalysis",
                "ChampionPatternAggregation",
                "ChampionMatchupLeadAggregation",
                "AccountRefresh",
                "MatchDataRetention"
            ]
        };

        // Open a fresh iteration for this whole pass so every ProcessRun recorded
        // below (across the per-process scopes) is stamped with the same id and the
        // admin can group them as one chain. The AsyncLocal id flows into each
        // awaited process; the scope restores the prior value when the pass ends.
        using var iteration = iterationContext.BeginIteration();
        logger.LogInformation("Starting iteration {IterationId}.", iteration.IterationId);

        foreach (var processName in sequence)
        {
            // A fresh scope per process gives each one its own DbContext and
            // scoped repositories. A single shared scope would let the
            // ChangeTracker accumulate every entity touched across the whole
            // sequence and leak cached scoped state from one process into the
            // next. The scope is disposed before moving on to the next process.
            await using var scope = scopeFactory.CreateAsyncScope();
            var processesByName = BuildProcessIndex(scope.ServiceProvider);

            if (!processesByName.TryGetValue(processName, out var process))
            {
                throw new InvalidOperationException(
                    $"No IIngestorProcess registered with Name '{processName}'. "
                    + $"Registered: {string.Join(", ", processesByName.Keys.Order(StringComparer.Ordinal))}.");
            }

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
                    processName);
            }
        }
    }

    private static IReadOnlyDictionary<string, IIngestorProcess> BuildProcessIndex(IServiceProvider serviceProvider)
    {
        // The per-process catch in RunModeAsync assumes every production
        // registration is wrapped in RecordedProcess (via AddRecordedProcess) so a
        // failure is still persisted as a Failed run. A process registered without
        // the wrapper still runs and logs, but its runs are invisible to process
        // health — always register through AddRecordedProcess.
        var index = new Dictionary<string, IIngestorProcess>(StringComparer.Ordinal);
        foreach (var process in serviceProvider.GetRequiredService<IEnumerable<IIngestorProcess>>())
        {
            if (!index.TryAdd(process.Name, process))
            {
                throw new InvalidOperationException(
                    $"Duplicate IIngestorProcess registration for Name '{process.Name}'.");
            }
        }

        return index;
    }

}
