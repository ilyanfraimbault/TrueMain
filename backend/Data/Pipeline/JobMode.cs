namespace Data.Pipeline;

/// <summary>
/// What an ingestor container runs (<c>Job:Mode</c>): one process, or a composite that
/// expands to several (<see cref="PipelineCatalog.StepsOwnedBy"/>).
/// </summary>
/// <remarks>
/// Read by name, but the numbers are pinned and never reused: a retired value stays
/// retired so an old number can never come back meaning another process.
/// </remarks>
public enum JobMode
{
    Full = 0,
    DiscoveryOnly = 1,
    ManualSeedOnly = 2,
    ScoringOnly = 3,
    MatchIngestionOnly = 4,
    MainAnalysisOnly = 5,
    PatternAggregationOnly = 6,
    AccountRefreshOnly = 7,
    MatchDataRetentionOnly = 8,
    HarvestOnly = 9,
    MatchupLeadAggregationOnly = 10,
    EloBracketEnrichmentOnly = 11,
    // 12 was PowerspikeAggregationOnly (retired).
    // 13 was TeamPositionCorrectionOnly (retired #2481).
    MainActivityOnly = 14,
    SynergyAggregationOnly = 15,
    BanAggregationOnly = 16,
    StorageSnapshotOnly = 17,
    // 18 was RunePageDeduplicationOnly (#1418), 19 LaneOutcomeAggregationOnly (#1445), both retired.
    LadderSyncOnly = 20,
    FetchLane = 21,
    AggregateLane = 22,
    CandidateStockSnapshotOnly = 23,
    ChampionProfileAggregationOnly = 24,
    ChampionItemContextAggregationOnly = 25,
    // 26 was RoleBoundItemBackfillOnly (retired #2481).
    PaceSamplingOnly = 27,
    OpponentAggregationOnly = 28,
    ChampionGameCountRefreshOnly = 29,
    FetchWorkers = 30,
    ScalingAggregationOnly = 31
}
