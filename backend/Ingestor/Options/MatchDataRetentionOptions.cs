namespace Ingestor.Options;

public class MatchDataRetentionOptions
{
    public const string SectionName = "MatchDataRetention";

    public int RetainedPatchCount { get; set; } = 2;

    /// <summary>
    /// Number of non-ranked matches deleted per transaction when draining queues
    /// other than the tracked one (<see cref="Core.Options.MainAnalysisOptions.QueueId"/>).
    /// Kept small so the cascading delete of timeline snapshots / kill positions
    /// never grows a single transaction's lock footprint or WAL into a spike that
    /// could re-fill a tight disk — the drain makes incremental, committed progress
    /// across batches (and across runs if interrupted).
    /// </summary>
    public int NonRankedDeleteBatchSize { get; set; } = 500;

    /// <summary>
    /// Number of most-recent patches whose champion aggregates (scopes+patterns,
    /// matchup stats, timeline leads, powerspike stats) are retained. <c>0</c>
    /// (the default) disables aggregate retention entirely: old-patch aggregates
    /// stay frozen forever, which is the production behaviour (#466) — they are
    /// the site's patch history and can never be recomputed once their raw
    /// matches are retired. Set to a positive value only on environments that
    /// must stay small (e.g. preprod), where history has no value.
    /// </summary>
    public int AggregateRetainedPatchCount { get; set; }

    /// <summary>
    /// Whether retention prunes a match's intermediate-minute timeline snapshots
    /// down to the canonical marks (5/10/15/20/30) once it has been folded into the
    /// powerspike aggregates (#694). Enabled by default: the dense per-minute grid
    /// only feeds that one-shot aggregation, so keeping it afterwards is pure waste
    /// (the grid otherwise grows to tens of GB). The canonical marks survive, so the
    /// timeline-leads / matchup-lead / match-detail reads are unaffected.
    /// </summary>
    public bool PruneAggregatedTimelineSnapshots { get; set; } = true;

    /// <summary>
    /// Number of aggregated matches whose intermediate-minute snapshots are pruned
    /// per transaction. Kept small so the one-off backfill delete (tens of millions
    /// of rows across the existing dense grid) makes incremental, committed progress
    /// without growing a single transaction's lock footprint or WAL.
    /// </summary>
    public int TimelineSnapshotPruneBatchSize { get; set; } = 500;
}
