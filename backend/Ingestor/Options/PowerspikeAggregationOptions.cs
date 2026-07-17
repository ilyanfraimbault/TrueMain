namespace Ingestor.Options;

public class PowerspikeAggregationOptions
{
    public const string SectionName = "PowerspikeAggregation";

    /// <summary>
    /// Number of pending matches loaded (with all their participants, timeline
    /// snapshots and item events) and folded into the powerspike aggregates per
    /// transaction. Kept modest so a batch's working set and its upsert / flag
    /// transaction stay bounded; the run loops batches until the per-run cap or the
    /// pending backlog is exhausted.
    /// </summary>
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>
    /// Upper bound on matches folded in a single run, so the initial backfill of the
    /// whole history is spread across scheduled runs instead of blocking one pass for
    /// hours. 0 means no cap (drain every pending match in one run).
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 20000;
}
