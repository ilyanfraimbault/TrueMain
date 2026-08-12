using Core.Lol.Lane;
namespace Ingestor.Options;

/// <summary>
/// Knobs for <c>ChampionLaneOutcomeAggregationProcess</c> (#919), the fold behind the
/// lane win rate shown beside the matchup game win rate.
/// </summary>
public class LaneOutcomeAggregationOptions
{
    public const string SectionName = "LaneOutcomeAggregation";

    /// <summary>
    /// Gold advantage at 15 minutes, over the lane opponent, above which the lane counts
    /// as won — and below its negative, as lost. A lane inside the band is neither:
    /// counted in <c>LaneGames</c> but in neither <c>LaneWins</c> nor <c>LaneLosses</c>,
    /// so it does not masquerade as a loss in the ratio.
    ///
    /// <para>
    /// 300 is roughly two camps or a wave and a half — small enough that a genuinely
    /// won lane clears it, large enough that a single lucky trade or one extra minion
    /// wave does not decide the number. Any threshold is arbitrary, which is exactly
    /// why it is configurable rather than a constant: it is a product judgement, and
    /// changing it re-defines every stored lane counter, so it should not be changed
    /// casually — old rows keep the outcome the threshold in force at fold time gave
    /// them, and frozen patches can never be recomputed (#466).
    /// </para>
    /// </summary>
    public int GoldLeadThreshold { get; set; } = LaneOutcomeRules.DefaultGoldLeadThreshold;

    /// <summary>
    /// Pending matches folded per transaction. Mirrors the sibling folds; the working set
    /// per match is the participant rows plus their 15-minute snapshots, so the same
    /// order of magnitude applies.
    /// </summary>
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>
    /// Upper bound on matches folded per run. The flag ships false for every retained
    /// match — the 15-minute snapshots are still there, so unlike #920's bans there is
    /// real history to pick up — and this spreads that initial drain across runs.
    /// 0 means no cap.
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 20000;
}
