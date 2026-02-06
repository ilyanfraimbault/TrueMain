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
            var discoveryProcess = scope.ServiceProvider.GetRequiredService<DiscoveryProcess>();
            var scoringProcess = scope.ServiceProvider.GetRequiredService<ScoringProcess>();
            var matchIngestionProcess = scope.ServiceProvider.GetRequiredService<MatchIngestionProcess>();
            var mainAnalysisProcess = scope.ServiceProvider.GetRequiredService<MainAnalysisProcess>();
            var accountRefreshProcess = scope.ServiceProvider.GetRequiredService<AccountRefreshProcess>();

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
                case JobMode.MainAnalysisOnly:
                    await mainAnalysisProcess.RunAsync(stoppingToken);
                    break;
                case JobMode.AccountRefreshOnly:
                    await accountRefreshProcess.RunAsync(stoppingToken);
                    break;
                default:
                    await discoveryProcess.RunAsync(stoppingToken);
                    await scoringProcess.RunAsync(stoppingToken);
                    await matchIngestionProcess.RunAsync(stoppingToken);
                    await mainAnalysisProcess.RunAsync(stoppingToken);
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
            "mainanalysisonly" => JobMode.MainAnalysisOnly,
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
        MainAnalysisOnly,
        AccountRefreshOnly
    }
}
