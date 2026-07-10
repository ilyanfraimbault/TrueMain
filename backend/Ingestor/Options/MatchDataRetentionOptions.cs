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
}
