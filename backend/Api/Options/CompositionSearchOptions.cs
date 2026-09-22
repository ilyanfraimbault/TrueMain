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
    /// Number of most-similar games kept for the build aggregation — applied
    /// <b>only when the draft pins no role opponent</b> (#1659). The pool is then the
    /// champion's recent games at the position rather than one matchup's, so hydrating
    /// thousands of unrelated games would be both slow and pointless: similarity alone
    /// decides, and past the first hundred it decides nothing. With the opponent pinned
    /// the selection keeps every game of the matchup instead, bounded by
    /// <see cref="CandidatePoolCap"/>.
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
    /// Upper bound on the candidate games scanned per request, most recent first. Bounds
    /// both the SQL join and the in-memory scoring pass — Postgres runs the scan
    /// single-threaded.
    ///
    /// <para>
    /// Since #1659 this is a guardrail rather than the selection rule. A pinned matchup
    /// holds 4 games at the median and 1 562 at the measured maximum, so the cap does not
    /// bite in practice; it is here so an unexpectedly huge pair — or an unpinned request
    /// on a very popular champion — cannot turn a page view into an unbounded fold.
    /// </para>
    /// </summary>
    public int CandidatePoolCap { get; set; } = 5_000;

    /// <summary>
    /// Vote weight of a winning game in the build aggregation (losses weigh
    /// 1). Weights only pick each dimension's winner — reported games and
    /// rates stay raw counts.
    /// </summary>
    public double WinWeight { get; set; } = 2d;

    /// <summary>
    /// Vote multiplier of a game played on the patch being recommended for. Crossed with
    /// <see cref="MainGameWeight"/>, so a main's game on the current patch outweighs a
    /// non-main's game on the previous one twelvefold — builds do not survive a patch, and
    /// a dedicated pilot's game says more about the build than an incidental one.
    /// </summary>
    public double CurrentPatchWeight { get; set; } = 3d;

    /// <summary>
    /// Vote multiplier of a game played on any older retained patch. Retention keeps two
    /// patches (<c>MatchDataRetention:RetainedPatchCount</c>), so in practice this is the
    /// one before the current; a straggler from further back weighs the same rather than
    /// being dropped, because dropping it would silently thin an already-sparse matchup.
    /// </summary>
    public double PreviousPatchWeight { get; set; } = 2d;

    /// <summary>
    /// Vote multiplier of a game piloted by an active main of the champion.
    /// </summary>
    public double MainGameWeight { get; set; } = 4d;

    /// <summary>
    /// Vote multiplier of a game piloted by anyone else.
    /// </summary>
    public double NonMainGameWeight { get; set; } = 1d;

    /// <summary>
    /// Extra vote weight granted at full draft similarity: a game's vote is
    /// multiplied by <c>1 + boost × (score / maxScore)</c>, so a perfect
    /// reproduction of the draft outweighs a barely-matching game by
    /// <c>1 + boost</c> while a slotless request leaves every game at 1.
    /// </summary>
    public double SimilarityWeightBoost { get; set; } = 3d;
}
