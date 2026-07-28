using Core.Options;
using Microsoft.Extensions.Options;

namespace Ingestor.Options;

public static class OptionsConfigurationExtensions
{
    public static IServiceCollection AddValidatedOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Shared platform scope (#496). Bound eagerly instead of read through
        // IOptions<PlatformScopeOptions> from each section, so an invalid shared list surfaces one
        // actionable error rather than the same failure cascading through every section that
        // inherits it. Both come from the same configuration section, which the ingestor reads
        // once at boot.
        var platformScope = configuration.GetSection(PlatformScopeOptions.SectionName).Get<PlatformScopeOptions>()
            ?? new PlatformScopeOptions();

        // Same reasoning for the ingested platforms the harvest is validated against: a validator
        // of MatchIngestionOptions may not depend on IOptions<MatchIngestionOptions>, since
        // building those options is what resolves the validator in the first place.
        var matchIngestionPlatforms = configuration
            .GetSection($"{MatchIngestionOptions.SectionName}:{nameof(MatchIngestionOptions.Platforms)}")
            .Get<List<string>>() ?? [];

        // Single owner of the cross-section platform invariants: it validates the shared scope and
        // every section that carries its own Platforms list, so a divergence fails the boot instead
        // of silently skipping a region for one pipeline stage. Registered as a plain instance —
        // it holds configuration data only, and depends on no service.
        var platformScopeValidator = new PlatformScopeValidator(platformScope, matchIngestionPlatforms);
        services.AddSingleton<IValidateOptions<PlatformScopeOptions>>(platformScopeValidator);
        services.AddSingleton<IValidateOptions<DiscoveryOptions>>(platformScopeValidator);
        services.AddSingleton<IValidateOptions<MatchIngestionOptions>>(platformScopeValidator);
        services.AddSingleton<IValidateOptions<HarvestOptions>>(platformScopeValidator);

        services.AddOptions<PlatformScopeOptions>()
            .Bind(configuration.GetSection(PlatformScopeOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<RiotOptions>()
            .Bind(configuration.GetSection(RiotOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Riot:ApiKey is required.")
            .Validate(options => options.MaxRetryAttempts is > 0 and <= 10, "Riot:MaxRetryAttempts must be between 1 and 10.")
            .Validate(options => options.AttemptTimeoutSeconds is > 0 and <= 600, "Riot:AttemptTimeoutSeconds must be between 1 and 600.")
            .Validate(options => options.TotalRequestTimeoutSeconds is > 0 and <= 3600, "Riot:TotalRequestTimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.TotalRequestTimeoutSeconds >= options.AttemptTimeoutSeconds,
                "Riot:TotalRequestTimeoutSeconds must be >= Riot:AttemptTimeoutSeconds.")
            .ValidateOnStart();

        services.AddOptions<CommunityDragonOptions>()
            .Bind(configuration.GetSection(CommunityDragonOptions.SectionName))
            .Validate(options => options.MaxRetryAttempts is > 0 and <= 10, "CommunityDragon:MaxRetryAttempts must be between 1 and 10.")
            .Validate(options => options.AttemptTimeoutSeconds is > 0 and <= 600, "CommunityDragon:AttemptTimeoutSeconds must be between 1 and 600.")
            .Validate(options => options.TotalRequestTimeoutSeconds is > 0 and <= 3600, "CommunityDragon:TotalRequestTimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.TotalRequestTimeoutSeconds > options.AttemptTimeoutSeconds,
                "CommunityDragon:TotalRequestTimeoutSeconds must be > CommunityDragon:AttemptTimeoutSeconds.")
            // The resilience handler divides the total budget across the attempts, so this
            // keeps every attempt worth at least a full second. Without it, a large retry
            // count against a small total would shrink the per-attempt timeout until every
            // attempt times out instantly — or, at the extreme, until the standard handler
            // rejects a sub-millisecond timeout and crash-loops the ingestor at startup.
            .Validate(
                options => options.TotalRequestTimeoutSeconds >= options.MaxRetryAttempts + 1,
                "CommunityDragon:TotalRequestTimeoutSeconds must be >= CommunityDragon:MaxRetryAttempts + 1, so every attempt gets at least one second.")
            .ValidateOnStart();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<DiscoveryOptions>()
            .Bind(configuration.GetSection(DiscoveryOptions.SectionName))
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .Validate(options => HasNonEmptyItems(options.TierScope), "Discovery:TierScope must contain at least one value.")
            .Validate(options => HasOnlyKnownTiers(options.TierScope), KnownTierScopeMessage)
            .Validate(options => options.TopChampionsPerAccount > 0, "Discovery:TopChampionsPerAccount must be greater than 0.")
            .Validate(options => options.MaxAccountsPerPlatformPerRun > 0, "Discovery:MaxAccountsPerPlatformPerRun must be greater than 0.")
            .Validate(options => options.SaveBatchSize > 0, "Discovery:SaveBatchSize must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<ManualSeedOptions>()
            .Bind(configuration.GetSection(ManualSeedOptions.SectionName))
            .Validate(options => options.BatchSize > 0, "ManualSeed:BatchSize must be greater than 0.")
            .Validate(options => options.TopChampionsPerAccount > 0, "ManualSeed:TopChampionsPerAccount must be greater than 0.")
            .Validate(options => options.MaxLastPlayDays >= 0, "ManualSeed:MaxLastPlayDays must be >= 0.")
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
            .Validate(options => options.HarvestObservedGamesLogNormalizer > 0,
                "Scoring:HarvestObservedGamesLogNormalizer must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<HarvestOptions>()
            .Bind(configuration.GetSection(HarvestOptions.SectionName))
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .Validate(options => options.QueueId > 0, "Harvest:QueueId must be greater than 0.")
            .Validate(options => options.MinObservedGames > 0, "Harvest:MinObservedGames must be greater than 0.")
            .Validate(options => options.MaxCandidatesPerRun > 0, "Harvest:MaxCandidatesPerRun must be greater than 0.")
            .Validate(options => options.NewCandidateShare is >= 0 and <= 1,
                "Harvest:NewCandidateShare must be between 0 and 1.")
            .Validate(options => options.SaveBatchSize > 0, "Harvest:SaveBatchSize must be greater than 0.")
            .Validate(options => options.LookbackDays >= 0, "Harvest:LookbackDays must be >= 0.")
            .ValidateOnStart();

        services.AddOptions<CoverageOptions>()
            .Bind(configuration.GetSection(CoverageOptions.SectionName))
            .Validate(options => options.TargetMainsPerChampion > 0,
                "Coverage:TargetMainsPerChampion must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<MatchIngestionOptions>()
            .Bind(configuration.GetSection(MatchIngestionOptions.SectionName))
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .Validate(options => options.BatchSize > 0, "MatchIngestion:BatchSize must be greater than 0.")
            .Validate(options => options.MatchesPerAccount > 0, "MatchIngestion:MatchesPerAccount must be greater than 0.")
            .Validate(options => options.SaveBatchSizeMatches > 0, "MatchIngestion:SaveBatchSizeMatches must be greater than 0.")
            .Validate(options => options.MaxMatchFetchConcurrency > 0, "MatchIngestion:MaxMatchFetchConcurrency must be greater than 0.")
            .Validate(options => options.ClaimLeaseMinutes > 0, "MatchIngestion:ClaimLeaseMinutes must be greater than 0.")
            .Validate(options => options.EstablishedMainShare is >= 0 and <= 1,
                "MatchIngestion:EstablishedMainShare must be between 0 and 1.")
            .ValidateOnStart();

        services.AddOptions<MainActivityOptions>()
            .Bind(configuration.GetSection(MainActivityOptions.SectionName))
            .Validate(options => options.BatchSize > 0, "MainActivity:BatchSize must be greater than 0.")
            .Validate(options => options.InactiveAfterDays > 0, "MainActivity:InactiveAfterDays must be greater than 0.")
            .Validate(options => options.RecheckAfterHours >= 0, "MainActivity:RecheckAfterHours must be >= 0.")
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
            .Validate(options => options.NonRankedDeleteBatchSize > 0, "MatchDataRetention:NonRankedDeleteBatchSize must be greater than 0.")
            .Validate(options => options.AggregateRetainedPatchCount >= 0, "MatchDataRetention:AggregateRetainedPatchCount must be >= 0 (0 disables aggregate retention).")
            .ValidateOnStart();

        services.AddOptions<CandidatePruningOptions>()
            .Bind(configuration.GetSection(CandidatePruningOptions.SectionName))
            .Validate(options => options.PruneAfterDays >= 0, "CandidatePruning:PruneAfterDays must be >= 0.")
            .ValidateOnStart();

        services.AddOptions<PowerspikeAggregationOptions>()
            .Bind(configuration.GetSection(PowerspikeAggregationOptions.SectionName))
            .Validate(options => options.MatchBatchSize > 0, "PowerspikeAggregation:MatchBatchSize must be greater than 0.")
            .Validate(options => options.MaxMatchesPerRun >= 0, "PowerspikeAggregation:MaxMatchesPerRun must be >= 0.")
            .ValidateOnStart();

        services.AddOptions<MatchupLeadAggregationOptions>()
            .Bind(configuration.GetSection(MatchupLeadAggregationOptions.SectionName))
            .Validate(options => options.MatchBatchSize > 0, "MatchupLeadAggregation:MatchBatchSize must be greater than 0.")
            .Validate(options => options.MaxMatchesPerRun >= 0, "MatchupLeadAggregation:MaxMatchesPerRun must be >= 0.")
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

    // GM and GRANDMASTER are accepted as synonyms — LadderDiscoveryService.FetchLadderEntriesAsync
    // checks for both. Anything else silently matched nothing at runtime (no warning), which is
    // exactly the kind of divergence #860 also guards against for unknown platform ids.
    private static readonly string[] KnownTiers = ["CHALLENGER", "GM", "GRANDMASTER", "MASTER"];

    private const string KnownTierScopeMessage =
        "Discovery:TierScope must contain only Master, GM (or Grandmaster) and/or Challenger — "
        + "the only tiers league-v4 exposes a dedicated ladder endpoint for.";

    private static bool HasOnlyKnownTiers(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .All(value => KnownTiers.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase));
    }
}
