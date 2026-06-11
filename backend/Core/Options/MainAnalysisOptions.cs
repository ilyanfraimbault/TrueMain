using Core.Lol.Map;

namespace Core.Options;

public class MainAnalysisOptions
{
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Number of accounts to process per database transaction.
    /// Higher values reduce transaction overhead but increase the amount of work lost on rollback.
    /// </summary>
    public int ProcessingBatchSize { get; set; } = 100;

    public int MatchesToConsider { get; set; } = 50;
    public LolQueueId QueueId { get; set; } = LolQueueId.RankedSoloDuo;
    public double PlayRateThreshold { get; set; } = 0.2;

    /// <summary>
    /// Lowest adaptive main threshold, applied to maximally under-covered champions
    /// (coverage deficit = 1). The effective threshold interpolates between
    /// <see cref="PlayRateThreshold"/> (covered champions) and this floor. Must be
    /// &lt;= <see cref="PlayRateThreshold"/>. Setting it equal to <see cref="PlayRateThreshold"/>
    /// disables the relaxation entirely: the interpolation becomes a no-op and no champion is
    /// ever classified as an extended sample.
    /// </summary>
    public double PlayRateFloor { get; set; } = 0.12;

    public double OtpPlayRateThreshold { get; set; } = 0.85;
    public double CriticalPlayRateThreshold { get; set; } = 0.1;
    public int MinMatchesToEvaluate { get; set; } = 20;
    public int RecomputeAfterHours { get; set; } = 24;
}
