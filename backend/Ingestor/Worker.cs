using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.Extensions.Options;

namespace Ingestor;

public sealed class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> jobOptions,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    private const string HeartbeatEnvironmentVariable = "INGESTOR_HEARTBEAT_PATH";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = jobOptions.Value;
        var mode = JobModeParser.Parse(options.Mode);

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
            await using var scope = scopeFactory.CreateAsyncScope();
            var processesByName = BuildProcessIndex(scope.ServiceProvider);
            await RunModeAsync(mode, processesByName, stoppingToken);
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

    private static async Task RunModeAsync(
        JobMode mode,
        IReadOnlyDictionary<string, IIngestorProcess> processesByName,
        CancellationToken stoppingToken)
    {
        var sequence = mode switch
        {
            JobMode.DiscoveryOnly => ["Discovery"],
            JobMode.ScoringOnly => ["Scoring"],
            JobMode.MatchIngestionOnly => ["MatchIngestion"],
            JobMode.MainAnalysisOnly => ["MainAnalysis"],
            JobMode.PatternAggregationOnly => ["ChampionPatternAggregation"],
            JobMode.AccountRefreshOnly => ["AccountRefresh"],
            JobMode.MatchDataRetentionOnly => ["MatchDataRetention"],
            _ => (string[])
            [
                "Discovery",
                "Scoring",
                "MatchIngestion",
                "MainAnalysis",
                "ChampionPatternAggregation",
                "AccountRefresh",
                "MatchDataRetention"
            ]
        };

        foreach (var processName in sequence)
        {
            if (!processesByName.TryGetValue(processName, out var process))
            {
                throw new InvalidOperationException(
                    $"No IIngestorProcess registered with Name '{processName}'. "
                    + $"Registered: {string.Join(", ", processesByName.Keys.Order(StringComparer.Ordinal))}.");
            }

            await process.RunCoreAsync(stoppingToken);
        }
    }

    private static IReadOnlyDictionary<string, IIngestorProcess> BuildProcessIndex(IServiceProvider serviceProvider)
    {
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
