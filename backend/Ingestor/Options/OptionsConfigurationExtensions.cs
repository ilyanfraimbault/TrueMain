using Core.Options;

namespace Ingestor.Options;

public static class OptionsConfigurationExtensions
{
    public static IServiceCollection AddValidatedOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RiotOptions>()
            .Bind(configuration.GetSection(RiotOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Riot:ApiKey is required.")
            .Validate(options => options.MaxRetryAttempts > 0, "Riot:MaxRetryAttempts must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<DiscoveryOptions>()
            .Bind(configuration.GetSection(DiscoveryOptions.SectionName))
            .Validate(options => HasNonEmptyItems(options.Platforms), "Discovery:Platforms must contain at least one value.")
            .Validate(options => HasNonEmptyItems(options.TierScope), "Discovery:TierScope must contain at least one value.")
            .Validate(options => options.TopChampionsPerAccount > 0, "Discovery:TopChampionsPerAccount must be greater than 0.")
            .Validate(options => options.MaxAccountsPerPlatformPerRun > 0, "Discovery:MaxAccountsPerPlatformPerRun must be greater than 0.")
            .Validate(options => options.SaveBatchSize > 0, "Discovery:SaveBatchSize must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<ScoringOptions>()
            .Bind(configuration.GetSection(ScoringOptions.SectionName))
            .Validate(options => options.TopNPerPlatform > 0, "Scoring:TopNPerPlatform must be greater than 0.")
            .Validate(options => options.TopChampionsPerAccount > 0, "Scoring:TopChampionsPerAccount must be greater than 0.")
            .Validate(options => options.BatchSize > 0, "Scoring:BatchSize must be greater than 0.")
            .Validate(options => options.RecencyWeight >= 0, "Scoring:RecencyWeight must be >= 0.")
            .Validate(options => options.RankWeight >= 0, "Scoring:RankWeight must be >= 0.")
            .Validate(options => options.PointsWeight >= 0, "Scoring:PointsWeight must be >= 0.")
            .Validate(options => options.ScarcityWeight >= 0, "Scoring:ScarcityWeight must be >= 0.")
            .Validate(options => options.RecencyWeight + options.RankWeight + options.PointsWeight + options.ScarcityWeight > 0,
                "Scoring weights sum (recency + rank + points + scarcity) must be greater than 0.")
            // Cross-property: scarcity must not outweigh the combined merit signal, for any
            // merit-weight configuration (not just the defaults that happen to sum to 1.0).
            .Validate(options => options.ScarcityWeight <= options.RecencyWeight + options.RankWeight + options.PointsWeight,
                "Scoring:ScarcityWeight must not exceed recency + rank + points, so scarcity cannot outweigh the combined merit signal.")
            .ValidateOnStart();

        services.AddOptions<CoverageOptions>()
            .Bind(configuration.GetSection(CoverageOptions.SectionName))
            .Validate(options => options.TargetMainsPerChampion > 0,
                "Coverage:TargetMainsPerChampion must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<MatchIngestionOptions>()
            .Bind(configuration.GetSection(MatchIngestionOptions.SectionName))
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
            .Validate(options => Enum.IsDefined(options.QueueId), "MainAnalysis:QueueId must be a defined LolQueueId.")
            .Validate(options => options.PlayRateThreshold is >= 0 and <= 1, "MainAnalysis:PlayRateThreshold must be in [0, 1].")
            .Validate(options => options.PlayRateFloor is >= 0 and <= 1, "MainAnalysis:PlayRateFloor must be in [0, 1].")
            .Validate(options => options.CriticalPlayRateThreshold is >= 0 and <= 1,
                "MainAnalysis:CriticalPlayRateThreshold must be in [0, 1].")
            // Cross-property constraints come after the individual range checks so a single
            // out-of-range value surfaces its own error rather than a confusing cross-property one.
            .Validate(options => options.PlayRateFloor <= options.PlayRateThreshold,
                "MainAnalysis:PlayRateFloor must be <= PlayRateThreshold.")
            .Validate(options => options.PlayRateFloor >= options.CriticalPlayRateThreshold,
                "MainAnalysis:PlayRateFloor must be >= CriticalPlayRateThreshold (otherwise extended-sample mains are demoted on the next cycle).")
            .Validate(options => options.MinMatchesToEvaluate > 0, "MainAnalysis:MinMatchesToEvaluate must be greater than 0.")
            .Validate(options => options.RecomputeAfterHours >= 0, "MainAnalysis:RecomputeAfterHours must be >= 0.")
            .ValidateOnStart();

        services.AddOptions<AccountRefreshOptions>()
            .Bind(configuration.GetSection(AccountRefreshOptions.SectionName))
            .Validate(options => options.BatchSize > 0, "AccountRefresh:BatchSize must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<MatchDataRetentionOptions>()
            .Bind(configuration.GetSection(MatchDataRetentionOptions.SectionName))
            .Validate(options => options.RetainedPatchCount > 0, "MatchDataRetention:RetainedPatchCount must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<JobOptions>()
            .Bind(configuration.GetSection(JobOptions.SectionName))
            .Validate(options => JobModeParser.TryParse(options.Mode, out _),
                $"Job:Mode must be one of: {string.Join(", ", Enum.GetNames<JobMode>())} (or the legacy alias RetentionOnly).")
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
