using Core.Lol.Lane;

namespace TrueMain.Options;

/// <summary>
/// Tuning knobs for the composition-based match search (#563). The similarity
/// weights are a starting point — tune on real examples before freezing them.
/// </summary>
public sealed class CompositionSearchOptions
{
    public const string SectionName = "CompositionSearch";

    /// <summary>
    /// Weight granted when the candidate game has the requested role opponent —
    /// the enemy at the player's own position. The single strongest signal: the
    /// direct matchup dominates itemization more than any other slot.
    /// </summary>
    public int RoleOpponentWeight { get; set; } = 10;

    /// <summary>
    /// Weight per matching enemy slot other than the role opponent.
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
    /// Gold gap at 15 minutes past which a sampled lane counts as decided (#1117).
    /// Shares its default with the ingestor's
    /// <c>LaneOutcomeAggregation:GoldLeadThreshold</c> through
    /// <see cref="LaneOutcomeRules.DefaultGoldLeadThreshold"/>, because "the lane was
    /// won" has to mean the same thing on this page as on the champion page.
    ///
    /// <para>
    /// A separate option rather than the ingestor's own: this one recomputes per
    /// request and can be changed freely, while changing the ingestor's re-defines
    /// every stored counter and cannot be applied retroactively (#919). A deployment
    /// that overrides one should override both, or the two figures part company.
    /// </para>
    /// </summary>
    public int LaneGoldLeadThreshold { get; set; } = LaneOutcomeRules.DefaultGoldLeadThreshold;

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
    /// Extra vote weight granted at full draft similarity: a game's vote is
    /// multiplied by <c>1 + boost × (score / maxScore)</c>, so a perfect
    /// reproduction of the draft outweighs a barely-matching game by
    /// <c>1 + boost</c> while a slotless request leaves every game at 1.
    /// </summary>
    public double SimilarityWeightBoost { get; set; } = 3d;
}
