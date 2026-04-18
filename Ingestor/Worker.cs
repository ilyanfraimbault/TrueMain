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
        var mode = JobModeParser.Parse(options.Mode);

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processesByName = BuildProcessIndex(scope.ServiceProvider);

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
