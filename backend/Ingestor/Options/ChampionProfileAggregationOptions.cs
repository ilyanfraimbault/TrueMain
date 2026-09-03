namespace Ingestor.Options;

/// <summary>
/// Batch sizing and thresholds for <c>ChampionProfileAggregationProcess</c> (#1449), the
/// incremental fold behind <c>champion_profile_stats</c>.
/// </summary>
public class ChampionProfileAggregationOptions
{
    public const string SectionName = "ChampionProfileAggregation";

    /// <summary>
    /// Number of pending matches folded per transaction. Same order of magnitude as the
    /// sibling folds: a batch loads ten slim participant rows and up to twenty 10/15-minute
    /// snapshots per match, never the ItemEvents jsonb.
    /// </summary>
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>
    /// Upper bound on matches folded in a single run; 0 means no cap. The flag ships false
    /// on every retained match, so the first runs drain a backlog the size of the retained
    /// history — most of it pre-#1448 rows that fold to nothing but still have to be
    /// flagged — before settling on the freshly-ingested tail.
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 20000;

    /// <summary>
    /// Base attack range at or above which a champion is stored as ranged. Melee
    /// champions sit at 125–200 (Rakan's 300 is the melee outlier), ranged ones at 425
    /// and up (Gnar's 400 is the ranged outlier), so the gap between 300 and 400 is where
    /// the line goes.
    /// </summary>
    public int RangedAttackRangeThreshold { get; set; } = 350;
}
