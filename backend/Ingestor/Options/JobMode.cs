namespace Ingestor.Options;

// Explicit values: Job:Mode is read by name (JobModeParser rejects numeric strings), but
// pinning the integer values keeps the existing modes stable if a value is ever persisted or
// surfaced numerically. HarvestOnly (#485) is appended with a new trailing value rather than
// inserted mid-enum, so the pre-existing modes keep their original numbers.
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
    PowerspikeAggregationOnly = 12,
    TeamPositionCorrectionOnly = 13,
    MainActivityOnly = 14,
    SynergyAggregationOnly = 15,
    BanAggregationOnly = 16,
    StorageSnapshotOnly = 17,

    // 18 was RunePageDeduplicationOnly, retired with its process in #1418: the rune-page
    // dimension can no longer hold a permutation duplicate, so there is nothing to repair.
    // The value stays retired rather than reused — the modes are stable identifiers.
    LaneOutcomeAggregationOnly = 19,
    LadderSyncOnly = 20,

    /// <summary>
    /// Everything that spends the Riot API budget, in pipeline order (#1362). Composite,
    /// like <see cref="Full"/>: it expands to a sub-sequence rather than to a process.
    /// </summary>
    FetchLane = 21,

    /// <summary>
    /// Everything that reads what the fetch lane wrote and turns it into aggregates, plus
    /// retention (#1362). Composite, like <see cref="Full"/>.
    /// </summary>
    AggregateLane = 22,

    /// <summary>
    /// Records the candidate stock per status into Mongo (#1403). A leaf process like
    /// the modes above, appended after the two composite ones rather than next to
    /// <see cref="StorageSnapshotOnly"/>, because the values are stable identifiers and
    /// inserting mid-enum would renumber them.
    /// </summary>
    CandidateStockSnapshotOnly = 23
}
