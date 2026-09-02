using Data.Configuration;
using Ingestor.Options;

namespace Ingestor.Configuration;

/// <summary>
/// The Ingestor's half of the effective-configuration allow-list (#1034): the pipeline
/// options that decide who gets discovered, scored, harvested, ingested and retained.
///
/// <para>
/// This is the process the admin portal cannot otherwise see into — its options classes
/// live in this assembly, which the Api does not reference — so the Ingestor publishes
/// its own snapshot at boot via <see cref="EffectiveConfigurationServiceCollectionExtensions.AddEffectiveConfigurationPublisher"/>
/// rather than the Api building it live.
/// </para>
/// </summary>
public static class IngestorEffectiveConfigurationCatalog
{
    // Declared ahead of Instance: static members initialize in declaration order, and
    // Instance's collection expression below reads every one of these.
    private static EffectiveConfigurationSectionDescriptor Discovery { get; } = new()
    {
        SectionName = DiscoveryOptions.SectionName,
        OptionsType = typeof(DiscoveryOptions),
        Title = "Discovery",
        Description =
            "How the apex-ladder crawl finds new accounts to track: tiers scraped, how many "
            + "per platform per run, the sliding-window offset that keeps a saturated ladder "
            + "from re-scanning its own top, and the profile-sync freshness below which the "
            + "per-entry summoner-v4 and champion-mastery calls are skipped as redundant."
    };

    private static EffectiveConfigurationSectionDescriptor LadderSync { get; } = new()
    {
        SectionName = LadderSyncOptions.SectionName,
        OptionsType = typeof(LadderSyncOptions),
        Title = "Ladder sync",
        Description =
            "Keeps stored ranks in step with the live ladder by reading the ladder instead of "
            + "one account at a time: which tiers are swept, and the per-run request budget the "
            + "paginated tiers below Master share."
    };

    private static EffectiveConfigurationSectionDescriptor Scoring { get; } = new()
    {
        SectionName = ScoringOptions.SectionName,
        OptionsType = typeof(ScoringOptions),
        Title = "Scoring",
        Description =
            "Ranks discovered candidates for the match-ingestion queue: the recency/rank/"
            + "points/scarcity weight blend, and the batch size a scoring pass processes."
    };

    private static EffectiveConfigurationSectionDescriptor Harvest { get; } = new()
    {
        SectionName = HarvestOptions.SectionName,
        OptionsType = typeof(HarvestOptions),
        Title = "Harvest",
        Description =
            "Turns orphan match_participants rows into candidates at near-zero Riot API "
            + "cost: the observed-games floor, and the per-run budget split between new "
            + "pairs and refreshes of pairs already tracked."
    };

    private static EffectiveConfigurationSectionDescriptor MatchIngestion { get; } = new()
    {
        SectionName = MatchIngestionOptions.SectionName,
        OptionsType = typeof(MatchIngestionOptions),
        Title = "Match ingestion",
        Description =
            "Claims and fetches match-v5 data for queued candidates: batch sizes, fetch "
            + "concurrency, the claim lease, and the share of each batch reserved for "
            + "established mains over freshly queued candidates."
    };

    private static EffectiveConfigurationSectionDescriptor LaneOutcomeAggregation { get; } = new()
    {
        SectionName = LaneOutcomeAggregationOptions.SectionName,
        OptionsType = typeof(LaneOutcomeAggregationOptions),
        Title = "Lane outcome aggregation",
        Description =
            "Folds the 15-minute gold-lead snapshot into a won/lost/neutral lane outcome: "
            + "the gold threshold that decides the verdict, and the per-run fold budget."
    };

    private static EffectiveConfigurationSectionDescriptor MatchDataRetention { get; } = new()
    {
        SectionName = MatchDataRetentionOptions.SectionName,
        OptionsType = typeof(MatchDataRetentionOptions),
        Title = "Match data retention",
        Description =
            "How many patches of raw match data (and, separately, of frozen champion "
            + "aggregates) are kept before retention deletes them, and the batch sizes that "
            + "keep each deletion pass from blowing a single transaction's lock footprint."
    };

    private static EffectiveConfigurationSectionDescriptor RiotRateLimit { get; } = new()
    {
        SectionName = RiotRateLimitOptions.SectionName,
        OptionsType = typeof(RiotRateLimitOptions),
        Title = "Riot rate limiting",
        Description =
            "How outbound Riot API calls are paced against the budget Riot enforces per "
            + "routing value: the limits assumed before Riot advertises its own, whether "
            + "per-endpoint method limits are enforced too, and the headroom held back so a "
            + "count we under-tracked does not become a 429."
    };

    public static EffectiveConfigurationCatalog Instance { get; } = new(
        ProcessName: "Ingestor",
        Sections:
        [
            SharedEffectiveConfigurationSections.MainAnalysis,
            SharedEffectiveConfigurationSections.Database,
            SharedEffectiveConfigurationSections.MongoLogging,
            RiotRateLimit,
            LadderSync,
            Discovery,
            Scoring,
            Harvest,
            MatchIngestion,
            LaneOutcomeAggregation,
            MatchDataRetention
        ]);
}
