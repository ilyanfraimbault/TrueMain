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
    /// Number of expired-patch matches deleted per transaction when a patch drops
    /// out of the retained window. A whole patch expiring is hundreds of thousands
    /// of matches; deleting them in one transaction let the cascading removal of
    /// timeline snapshots / kill positions blow the command timeout on every run,
    /// and the rollback meant retention never made progress (#988).
    /// </summary>
    public int ExpiredPatchDeleteBatchSize { get; set; } = 500;

    /// <summary>
    /// Number of most-recent patches whose champion aggregates (scopes+patterns,
    /// matchup stats, synergy stats, ban stats) are retained. <c>0</c>
    /// (the default) disables aggregate retention entirely: old-patch aggregates
    /// stay frozen forever, which is the production behaviour (#466) — they are
    /// the site's patch history and can never be recomputed once their raw
    /// matches are retired. Set to a positive value only on environments that
    /// must stay small (e.g. preprod), where history has no value.
    /// </summary>
    public int AggregateRetainedPatchCount { get; set; }

    /// <summary>
    /// Whether retention prunes a timeline-ingested match's snapshots down to the
    /// canonical marks (5/10/15/20/30) (#694). Enabled by default: matches ingested
    /// while the dense per-minute grid was still written carry intermediate minutes
    /// nothing reads (the grid grew to tens of GB). The canonical marks survive, so
    /// every snapshot reader is unaffected.
    /// </summary>
    public bool PruneTimelineSnapshots { get; set; } = true;

    /// <summary>
    /// Number of matches whose intermediate-minute snapshots are pruned
    /// per transaction. Kept small so the one-off backfill delete (tens of millions
    /// of rows across the existing dense grid) makes incremental, committed progress
    /// without growing a single transaction's lock footprint or WAL.
    /// </summary>
    public int TimelineSnapshotPruneBatchSize { get; set; } = 500;
}
