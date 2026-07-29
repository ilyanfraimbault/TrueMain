namespace Ingestor.Options;

/// <summary>
/// Batch sizing for <c>ChampionBanAggregationProcess</c> (#920), the incremental
/// fold behind <c>champion_ban_stats</c>.
/// </summary>
public class BanAggregationOptions
{
    public const string SectionName = "BanAggregation";

    /// <summary>
    /// Number of pending matches folded into the ban aggregates per transaction.
    /// Mirrors <see cref="SynergyAggregationOptions.MatchBatchSize"/>; the working
    /// set per match is smaller still (ten ban rows and the participants' elo bands,
    /// no per-participant fan-out), so the same order of magnitude is comfortable.
    /// </summary>
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>
    /// Upper bound on matches folded in a single run. Unlike the synergy fold there
    /// is no historical backlog to drain — the flag ships true for every existing
    /// match, since bans could not be backfilled — so this only ever caps the
    /// matches ingested since the previous run. 0 means no cap.
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 20000;
}
