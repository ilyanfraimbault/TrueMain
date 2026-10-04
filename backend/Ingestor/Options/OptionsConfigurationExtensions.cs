using Core.Lol.Ranking;
using Core.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddSingleton<IValidateOptions<LadderSyncOptions>>(platformScopeValidator);
        services.AddSingleton<IValidateOptions<MatchIngestionOptions>>(platformScopeValidator);
        services.AddSingleton<IValidateOptions<HarvestOptions>>(platformScopeValidator);

        services.AddOptions<PlatformScopeOptions>()
            .Bind(configuration.GetSection(PlatformScopeOptions.SectionName))
            .ValidateOnStart();

        // Range / Required rules live as attributes on each options class, checked by the
        // source-generated validators in OptionsValidators.cs (#271). What stays below is what an
        // attribute cannot express: cross-field invariants, conditional rules and custom parsing.
        services.AddOptionsWithValidator<RiotOptions, RiotOptionsValidator>(configuration, RiotOptions.SectionName)
            .Validate(
                options => options.TotalRequestTimeoutSeconds >= options.AttemptTimeoutSeconds,
                "Riot:TotalRequestTimeoutSeconds must be >= Riot:AttemptTimeoutSeconds.")
            .ValidateOnStart();

        services.AddOptionsWithValidator<RiotRateLimitOptions, RiotRateLimitOptionsValidator>(
                configuration, RiotRateLimitOptions.SectionName)
            .Validate(
                options => !options.Enabled || RiotRateLimitOptionsValidation.HasParsableWindow(options.AppLimits),
                "RiotRateLimit:AppLimits must contain at least one \"requests:seconds\" window, e.g. \"20:1,100:120\".")
            .ValidateOnStart();

        services.AddOptionsWithValidator<CommunityDragonOptions, CommunityDragonOptionsValidator>(
                configuration, CommunityDragonOptions.SectionName)
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

        services.AddOptionsWithValidator<DiscoveryOptions, DiscoveryOptionsValidator>(configuration, DiscoveryOptions.SectionName)
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .Validate(options => HasNonEmptyItems(options.TierScope), "Discovery:TierScope must contain at least one value.")
            .Validate(options => HasOnlyKnownTiers(options.TierScope), KnownTierScopeMessage)
            .ValidateOnStart();

        services.AddOptionsWithValidator<LadderSyncOptions, LadderSyncOptionsValidator>(configuration, LadderSyncOptions.SectionName)
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .Validate(options => HasNonEmptyItems(options.TierScope), "LadderSync:TierScope must contain at least one value.")
            .Validate(options => HasOnlyKnownLadderTiers(options.TierScope), KnownLadderTierScopeMessage)
            .ValidateOnStart();

        services.AddOptionsWithValidator<ManualSeedOptions, ManualSeedOptionsValidator>(configuration, ManualSeedOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<ScoringOptions, ScoringOptionsValidator>(configuration, ScoringOptions.SectionName)
            .Validate(options => options.RecencyWeight + options.RankWeight + options.PointsWeight + options.ScarcityWeight > 0,
                "Scoring weights sum (recency + rank + points + scarcity) must be greater than 0.")
            // Cross-property: scarcity must not outweigh the combined merit signal, for any
            // merit-weight configuration (not just the defaults that happen to sum to 1.0).
            .Validate(options => options.ScarcityWeight <= options.RecencyWeight + options.RankWeight + options.PointsWeight,
                "Scoring:ScarcityWeight must not exceed recency + rank + points, so scarcity cannot outweigh the combined merit signal.")
            .ValidateOnStart();

        services.AddOptionsWithValidator<HarvestOptions, HarvestOptionsValidator>(configuration, HarvestOptions.SectionName)
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .ValidateOnStart();

        services.AddOptionsWithValidator<CoverageOptions, CoverageOptionsValidator>(configuration, CoverageOptions.SectionName)
            .ValidateOnStart();

        // MatchesPerAccount's upper bound (100) is Riot's own: the ids endpoint rejects a larger
        // count. The client clamps too, but a configured 500 silently ingesting 100 is worth failing on.
        services.AddOptionsWithValidator<MatchIngestionOptions, MatchIngestionOptionsValidator>(
                configuration, MatchIngestionOptions.SectionName)
            .PostConfigure(options => options.Platforms = platformScope.Resolve(options.Platforms))
            .ValidateOnStart();

        services.AddOptionsWithValidator<MainActivityOptions, MainActivityOptionsValidator>(configuration, MainActivityOptions.SectionName)
            .ValidateOnStart();

        // PlayRateFloor's upper bound is exclusive: DedicationScore.Commitment divides by
        // (1 - floor), so a floor of exactly 1 would divide by zero (#930 review — this bound used
        // to be the loose <= 1, disagreeing with the Api's stricter < 1 on the same option).
        services.AddOptionsWithValidator<MainAnalysisOptions, MainAnalysisOptionsValidator>(configuration, "MainAnalysis")
            .Validate(options => Enum.IsDefined(options.QueueId), "MainAnalysis:QueueId must be a defined LolQueueId.")
            // Cross-property constraints: the generated validator reports each out-of-range value
            // under its own name, so a single bad value is never explained only by these.
            .Validate(options => options.PlayRateFloor <= options.PlayRateThreshold,
                "MainAnalysis:PlayRateFloor must be <= PlayRateThreshold.")
            .Validate(options => options.PlayRateFloor >= options.CriticalPlayRateThreshold,
                "MainAnalysis:PlayRateFloor must be >= CriticalPlayRateThreshold (otherwise extended-sample mains are demoted on the next cycle).")
            .ValidateOnStart();

        services.AddOptionsWithValidator<AccountRefreshOptions, AccountRefreshOptionsValidator>(
                configuration, AccountRefreshOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<MatchDataRetentionOptions, MatchDataRetentionOptionsValidator>(
                configuration, MatchDataRetentionOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<CandidatePruningOptions, CandidatePruningOptionsValidator>(
                configuration, CandidatePruningOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<IntakeOptions, IntakeOptionsValidator>(configuration, IntakeOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<MatchupLeadAggregationOptions, MatchupLeadAggregationOptionsValidator>(
                configuration, MatchupLeadAggregationOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<SynergyAggregationOptions, SynergyAggregationOptionsValidator>(
                configuration, SynergyAggregationOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<LaneOutcomeAggregationOptions, LaneOutcomeAggregationOptionsValidator>(
                configuration, LaneOutcomeAggregationOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<BanAggregationOptions, BanAggregationOptionsValidator>(
                configuration, BanAggregationOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<ChampionProfileAggregationOptions, ChampionProfileAggregationOptionsValidator>(
                configuration, ChampionProfileAggregationOptions.SectionName)
            .ValidateOnStart();

        services.AddOptionsWithValidator<ItemContextAggregationOptions, ItemContextAggregationOptionsValidator>(
                configuration, ItemContextAggregationOptions.SectionName)
            .Validate(options => options.MinPickRate < options.CoreRate, "ItemContextAggregation:MinPickRate must be below CoreRate.")
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

    // Binds the section and registers the options class's source-generated validator. The
    // TryAddEnumerable keeps a second call for the same pair from validating twice.
    private static OptionsBuilder<TOptions> AddOptionsWithValidator<TOptions, TValidator>(
        this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
        return services.AddOptions<TOptions>().Bind(configuration.GetSection(sectionName));
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

    // Unlike Discovery, the ladder sync can page any ranked tier, so its scope is validated
    // against the full ladder rather than the three apex tiers.
    private const string KnownLadderTierScopeMessage =
        "LadderSync:TierScope must contain only Riot ranked tiers (Iron..Challenger), with GM "
        + "accepted as shorthand for Grandmaster.";

    private static bool HasOnlyKnownLadderTiers(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .All(value =>
            {
                var tier = value.Trim();
                return string.Equals(tier, "GM", StringComparison.OrdinalIgnoreCase)
                    || EloBracket.Ladder.Contains(tier.ToUpperInvariant(), StringComparer.Ordinal);
            });
    }

    private static bool HasOnlyKnownTiers(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .All(value => KnownTiers.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase));
    }
}
