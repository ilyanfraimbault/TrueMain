namespace Ingestor.Options;

/// <summary>
/// Batch sizing for <c>ChampionSynergyAggregationProcess</c> (#922), the incremental
/// fold behind <c>champion_synergy_stats</c>.
/// </summary>
public class SynergyAggregationOptions
{
    public const string SectionName = "SynergyAggregation";

    /// <summary>
    /// Number of pending matches loaded with their participants and folded into the
    /// synergy aggregates per transaction. Mirrors
    /// <see cref="MatchupLeadAggregationOptions.MatchBatchSize"/>, and is deliberately
    /// the same order of magnitude even though a synergy fold emits ~4 pair rows per
    /// tracked participant instead of one: the working set is still just the
    /// participant rows of the batch (no timeline JSON), and the upserts are grouped
    /// by key before they leave the process, so a batch's array parameters stay
    /// bounded by the number of distinct keys it touched, not by the fold count.
    /// </summary>
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>
    /// Upper bound on matches folded in a single run, so the initial backfill — which
    /// here is the whole retained history, since the fold flag ships false for every
    /// existing match — is spread across scheduled runs instead of blocking one
    /// pipeline pass for hours. 0 means no cap (drain every pending match in one run).
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 20000;
}
