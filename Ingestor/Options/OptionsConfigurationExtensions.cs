using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Options;

public static class OptionsConfigurationExtensions
{
    private static readonly HashSet<string> SupportedJobModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Full",
        "DiscoveryOnly",
        "ScoringOnly",
        "MatchIngestionOnly",
        "MainAnalysisOnly",
        "AccountRefreshOnly"
    };

    public static IServiceCollection AddValidatedOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RiotOptions>()
            .Bind(configuration.GetSection("Riot"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Riot:ApiKey is required.")
            .Validate(options => options.MaxRetryAttempts > 0, "Riot:MaxRetryAttempts must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection("Seed"))
            .ValidateOnStart();

        services.AddOptions<DiscoveryOptions>()
            .Bind(configuration.GetSection("Discovery"))
            .Validate(options => HasNonEmptyItems(options.Platforms), "Discovery:Platforms must contain at least one value.")
            .Validate(options => HasNonEmptyItems(options.TierScope), "Discovery:TierScope must contain at least one value.")
            .Validate(options => options.TopChampionsPerAccount > 0, "Discovery:TopChampionsPerAccount must be greater than 0.")
            .Validate(options => options.MaxAccountsPerPlatformPerRun > 0, "Discovery:MaxAccountsPerPlatformPerRun must be greater than 0.")
            .Validate(options => options.SaveBatchSize > 0, "Discovery:SaveBatchSize must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<ScoringOptions>()
            .Bind(configuration.GetSection("Scoring"))
            .Validate(options => options.TopNPerPlatform > 0, "Scoring:TopNPerPlatform must be greater than 0.")
            .Validate(options => options.TopChampionsPerAccount > 0, "Scoring:TopChampionsPerAccount must be greater than 0.")
            .Validate(options => options.BatchSize > 0, "Scoring:BatchSize must be greater than 0.")
            .Validate(options => options.RecencyWeight >= 0, "Scoring:RecencyWeight must be >= 0.")
            .Validate(options => options.RankWeight >= 0, "Scoring:RankWeight must be >= 0.")
            .Validate(options => options.PointsWeight >= 0, "Scoring:PointsWeight must be >= 0.")
            .Validate(options => options.RecencyWeight + options.RankWeight + options.PointsWeight > 0,
                "Scoring weights sum must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<MatchIngestionOptions>()
            .Bind(configuration.GetSection("MatchIngestion"))
            .Validate(options => options.BatchSize > 0, "MatchIngestion:BatchSize must be greater than 0.")
            .Validate(options => options.MatchesPerAccount > 0, "MatchIngestion:MatchesPerAccount must be greater than 0.")
            .Validate(options => options.SaveBatchSizeMatches > 0, "MatchIngestion:SaveBatchSizeMatches must be greater than 0.")
            .Validate(options => options.ClaimLeaseMinutes > 0, "MatchIngestion:ClaimLeaseMinutes must be greater than 0.")
            .Validate(options => HasNonEmptyItems(options.Platforms), "MatchIngestion:Platforms must contain at least one value.")
            .ValidateOnStart();

        services.AddOptions<MainAnalysisOptions>()
            .Bind(configuration.GetSection("MainAnalysis"))
            .Validate(options => options.BatchSize > 0, "MainAnalysis:BatchSize must be greater than 0.")
            .Validate(options => options.ProcessingBatchSize > 0, "MainAnalysis:ProcessingBatchSize must be greater than 0.")
            .Validate(options => options.MatchesToConsider > 0, "MainAnalysis:MatchesToConsider must be greater than 0.")
            .Validate(options => options.QueueId > 0, "MainAnalysis:QueueId must be greater than 0.")
            .Validate(options => options.PlayRateThreshold is >= 0 and <= 1, "MainAnalysis:PlayRateThreshold must be in [0, 1].")
            .Validate(options => options.CriticalPlayRateThreshold is >= 0 and <= 1,
                "MainAnalysis:CriticalPlayRateThreshold must be in [0, 1].")
            .Validate(options => options.MinMatchesToEvaluate > 0, "MainAnalysis:MinMatchesToEvaluate must be greater than 0.")
            .Validate(options => options.RecomputeAfterHours >= 0, "MainAnalysis:RecomputeAfterHours must be >= 0.")
            .ValidateOnStart();

        services.AddOptions<AccountRefreshOptions>()
            .Bind(configuration.GetSection("AccountRefresh"))
            .Validate(options => options.BatchSize > 0, "AccountRefresh:BatchSize must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<JobOptions>()
            .Bind(configuration.GetSection("Job"))
            .Validate(options => SupportedJobModes.Contains(options.Mode),
                $"Job:Mode must be one of: {string.Join(", ", SupportedJobModes)}")
            .Validate(options => options.RunOnce || (options.IntervalMinutes.HasValue && options.IntervalMinutes > 0),
                "Job:IntervalMinutes must be greater than 0 when RunOnce is false.")
            .ValidateOnStart();

        return services;
    }

    private static bool HasNonEmptyItems(IEnumerable<string> values)
    {
        return values.Any(value => !string.IsNullOrWhiteSpace(value));
    }
}
