using Core.Lol.Map;
using Core.Truemains;

namespace Core.Options;

public class MainAnalysisOptions
{
    /// <summary>
    /// Whether the champion pattern aggregation folds the <em>non-main</em>
    /// population alongside the mains (#1346's truemains toggle).
    ///
    /// <para>
    /// Off by default, and deliberately so. This process runs on every cycle of
    /// the full pipeline, so widening it is not a decision someone takes when
    /// they are ready — it takes effect on the next run after a deploy. On
    /// production the widening multiplies the source rows by ~4.3 (438k → 1.87M),
    /// and this is the process whose working set once reached ~6 GB and got
    /// OOM-killed, taking the VPS with it (#601). The per-champion chunking that
    /// fixed it has not been measured at that volume, so the flag is the gate
    /// that lets it be measured first.
    /// </para>
    ///
    /// <para>
    /// While it is off the aggregate holds mains only — exactly what it held
    /// before #1346 — and the truemains toggle's "everyone" state returns the
    /// same rows as "truemains". Reads are unaffected either way: they filter on
    /// the persisted flag, which is simply always <c>true</c> until this is
    /// turned on.
    /// </para>
    /// </summary>
    public bool AggregateNonMainPopulation { get; set; }

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
    /// <para>
    /// This is also the floor the dedication score rescales commitment from, so the bottom of that
    /// scale stays reachable. The default is shared with <see cref="DedicationScore.CommitmentFloor"/>
    /// and the configured value is threaded into scoring, so retuning this option moves both (#869).
    /// </para>
    /// </summary>
    public double PlayRateFloor { get; set; } = DedicationScore.CommitmentFloor;

    public double OtpPlayRateThreshold { get; set; } = 0.85;
    public double CriticalPlayRateThreshold { get; set; } = 0.1;
    public int MinMatchesToEvaluate { get; set; } = 20;
    public int RecomputeAfterHours { get; set; } = 24;
}
