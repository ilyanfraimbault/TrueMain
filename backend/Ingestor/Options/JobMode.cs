namespace Ingestor.Options;

public enum JobMode
{
    Full = 0,
    DiscoveryOnly,
    ScoringOnly,
    MatchIngestionOnly,
    MainAnalysisOnly,
    PatternAggregationOnly,
    AccountRefreshOnly,
    MatchDataRetentionOnly
}
