using Core.Lol.Lane;
namespace Ingestor.Options;

/// <summary>
/// The one knob behind the lane win rate shown beside the matchup game win rate (#919).
///
/// <para>
/// It keeps its own section although the fold that reads it now lives inside
/// <c>ChampionMatchupLeadAggregationProcess</c> (#1445): the threshold is a product
/// judgement about what "won the lane" means — shared verbatim with the API's live pass
/// over a composition's games — not a pacing knob for a process, and moving the key
/// would rename it on every deployed host for no gain. The batching knobs that used to
/// sit beside it went with the process; the merged fold paces off
/// <see cref="MatchupLeadAggregationOptions"/>.
/// </para>
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
}
