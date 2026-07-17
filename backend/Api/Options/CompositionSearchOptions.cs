namespace TrueMain.Options;

/// <summary>
/// Tuning knobs for the composition-based match search (#563). The similarity
/// weights are a starting point — tune on real examples before freezing them.
/// </summary>
public sealed class CompositionSearchOptions
{
    public const string SectionName = "CompositionSearch";

    /// <summary>
    /// Weight granted when the candidate game has the requested lane opponent —
    /// the enemy at the player's own position. The single strongest signal: the
    /// direct matchup dominates itemization more than any other slot.
    /// </summary>
    public int LaneOpponentWeight { get; set; } = 10;

    /// <summary>
    /// Weight per matching enemy slot other than the lane opponent.
    /// </summary>
    public int EnemyWeight { get; set; } = 4;

    /// <summary>
    /// Weight per matching ally slot (the four teammates besides the player).
    /// </summary>
    public int AllyWeight { get; set; } = 2;

    /// <summary>
    /// Number of most-similar games kept for the build aggregation.
    /// </summary>
    public int TopK { get; set; } = 100;

    /// <summary>
    /// Upper bound on the candidate games scanned per request, most recent
    /// first. Bounds both the SQL join and the in-memory scoring pass — the
    /// full pool for a popular champion is far larger than what recency-relevant
    /// similarity ranking needs, and Postgres runs the scan single-threaded.
    /// </summary>
    public int CandidatePoolCap { get; set; } = 5_000;

    /// <summary>
    /// Vote weight of a winning game in the build aggregation (losses weigh
    /// 1). Weights only pick each dimension's winner — reported games and
    /// rates stay raw counts.
    /// </summary>
    public double WinWeight { get; set; } = 2d;

    /// <summary>
    /// Number of situational (non-core) items surfaced by the aggregation.
    /// </summary>
    public int SituationalItemCount { get; set; } = 5;
}
