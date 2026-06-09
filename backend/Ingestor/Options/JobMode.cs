namespace Ingestor.Options;

public enum JobMode
{
    Full = 0,
    DiscoveryOnly,
    ManualSeedOnly,
    ScoringOnly,
    MatchIngestionOnly,
    MainAnalysisOnly,
    PatternAggregationOnly,
    AccountRefreshOnly,
    MatchDataRetentionOnly
}
