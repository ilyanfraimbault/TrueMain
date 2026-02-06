using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.Extensions.Options;

namespace Ingestor;

public class Worker(
    ILogger<Worker> logger,
    DiscoveryProcess discoveryProcess,
    ScoringProcess scoringProcess,
    MatchIngestionProcess matchIngestionProcess,
    AccountRefreshProcess accountRefreshProcess,
    IOptions<DiscoveryOptions> discoveryOptions,
    IOptions<JobOptions> jobOptions,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = discoveryOptions.Value;
        var mode = NormalizeMode(jobOptions.Value.Mode);

        do
        {
            switch (mode)
            {
                case JobMode.DiscoveryOnly:
                    await discoveryProcess.RunAsync(stoppingToken);
                    break;
                case JobMode.ScoringOnly:
                    await scoringProcess.RunAsync(stoppingToken);
                    break;
                case JobMode.MatchIngestionOnly:
                    await matchIngestionProcess.RunAsync(stoppingToken);
                    break;
                case JobMode.AccountRefreshOnly:
                    await accountRefreshProcess.RunAsync(stoppingToken);
                    break;
                default:
                    await discoveryProcess.RunAsync(stoppingToken);
                    await scoringProcess.RunAsync(stoppingToken);
                    await matchIngestionProcess.RunAsync(stoppingToken);
                    await accountRefreshProcess.RunAsync(stoppingToken);
                    break;
            }

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
            "accountrefreshonly" => JobMode.AccountRefreshOnly,
            "full" => JobMode.Full,
            _ => JobMode.Full
        };
    }

    private enum JobMode
    {
        Full,
        DiscoveryOnly,
        ScoringOnly,
        MatchIngestionOnly,
        AccountRefreshOnly
    }
}
