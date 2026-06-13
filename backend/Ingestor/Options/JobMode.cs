namespace Ingestor.Options;

public enum JobMode
{
    Full = 0,
    DiscoveryOnly,
    ManualSeedOnly,
    HarvestOnly,
    ScoringOnly,
    MatchIngestionOnly,
    MainAnalysisOnly,
    PatternAggregationOnly,
    AccountRefreshOnly,
    MatchDataRetentionOnly
}
