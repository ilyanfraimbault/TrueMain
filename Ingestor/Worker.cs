using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.Extensions.Options;

namespace Ingestor;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> jobOptions,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = jobOptions.Value;
        var mode = NormalizeMode(options.Mode);

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processesByName = scope.ServiceProvider
                .GetRequiredService<IEnumerable<IIngestorProcess>>()
                .ToDictionary(process => process.Name, StringComparer.Ordinal);

            await RunModeAsync(mode, processesByName, stoppingToken);

            if (options.RunOnce)
            {
                break;
            }

            var delayMinutes = options.IntervalMinutes is > 0 ? options.IntervalMinutes.Value : 60;
            logger.LogInformation("Run completed. Waiting {DelayMinutes} minutes before next run.", delayMinutes);
            await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);

        applicationLifetime.StopApplication();
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
            await processesByName[processName].RunCoreAsync(stoppingToken);
        }
    }

    private static JobMode NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return JobMode.Full;
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "discoveryonly" => JobMode.DiscoveryOnly,
            "scoringonly" => JobMode.ScoringOnly,
            "matchingestiononly" => JobMode.MatchIngestionOnly,
            "mainanalysisonly" => JobMode.MainAnalysisOnly,
            "patternaggregationonly" => JobMode.PatternAggregationOnly,
            "accountrefreshonly" => JobMode.AccountRefreshOnly,
            "matchdataretentiononly" => JobMode.MatchDataRetentionOnly,
            "retentiononly" => JobMode.MatchDataRetentionOnly,
            "full" => JobMode.Full,
            _ => throw new InvalidOperationException($"Unsupported job mode '{mode}'.")
        };
    }

    private enum JobMode
    {
        Full,
        DiscoveryOnly,
        ScoringOnly,
        MatchIngestionOnly,
        MainAnalysisOnly,
        PatternAggregationOnly,
        AccountRefreshOnly,
        MatchDataRetentionOnly
    }
}
