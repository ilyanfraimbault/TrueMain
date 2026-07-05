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
    MatchupLeadAggregationOnly = 10
}
