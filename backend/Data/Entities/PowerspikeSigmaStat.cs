namespace Data.Entities;

/// <summary>
/// Global per-minute spread of the gold / damage lead across the whole
/// tracked-account population on a queue — the normaliser that makes the two leads
/// comparable in the power blend (#694). One row per (queue, interval-minute),
/// ~30 rows per queue.
///
/// Stores the running sums needed to recover a sample stddev without the raw
/// snapshots: <c>σ = sqrt((SumSq − Sum²/n) / (n − 1))</c>. Accumulated additively
/// per lane pair at aggregation time; because the snapshots are pruned afterwards
/// it becomes a lifetime average rather than a live 2-patch window — acceptable for
/// a slowly-changing per-minute scale (rebuild periodically if it ever drifts).
/// </summary>
public class PowerspikeSigmaStat
{
    public Guid Id { get; set; }

    public int QueueId { get; set; }

    /// <summary>Minute mark (1..30).</summary>
    public int IntervalMinute { get; set; }

    public double SumGoldDiff { get; set; }

    public double SumGoldDiffSq { get; set; }

    public double SumDamageDiff { get; set; }

    public double SumDamageDiffSq { get; set; }

    /// <summary>Number of directed lane-pair samples folded in at this mark.</summary>
    public long SampleCount { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
