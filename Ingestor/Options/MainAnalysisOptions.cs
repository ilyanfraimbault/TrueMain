namespace Ingestor.Options;

public class MainAnalysisOptions
{
    public int BatchSize { get; set; } = 100;
    public int MatchesToConsider { get; set; } = 50;
    public int QueueId { get; set; } = 420;
    public double PlayRateThreshold { get; set; } = 0.2;
    public int MinMatchesToEvaluate { get; set; } = 20;
    public int RecomputeAfterHours { get; set; } = 24;
}
