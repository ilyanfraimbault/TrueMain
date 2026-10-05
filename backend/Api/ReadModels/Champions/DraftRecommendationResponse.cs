namespace TrueMain.ReadModels.Champions;

/// <summary>One enemy champion placed in a lane, and how sure we are.</summary>
public sealed record DraftEnemyLaneReadModel
{
    public int ChampionId { get; init; }

    /// <summary>Riot team position the guesser placed it in.</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// 0..1. Half means a coin flip between two readings, not "half right" —
    /// the panel must render it as a guess, because the matchup, runes and build
    /// underneath all rest on it.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>True when the user corrected this slot by hand.</summary>
    public bool Pinned { get; init; }
}

/// <summary>
/// A pick we could make, and why it ranks where it does.
///
/// The components stay separate all the way to the client on purpose: the
/// panel has to be able to say a pick is good *because the lane is favourable*,
/// *because it is safe blind* or *because it fits the team*, which a single total
/// cannot express. Every figure is a measured win-rate difference in probability
/// points with the games behind it; none is a win probability.
/// </summary>
public sealed record DraftCandidateReadModel
{
    public int ChampionId { get; init; }

    /// <summary>
    /// Win rate against the enemy in our lane minus this champion's win rate at
    /// the lane overall, both from <c>champion_matchup_stats</c>. When the lane
    /// guess is split between several enemies, each one's delta weighted by the
    /// chance the solver gives it of being our opponent (#1906).
    /// </summary>
    public double MatchupDelta { get; init; }

    /// <summary>Games the matchup rests on. Zero means no enemy in our lane, or the pairing is unseen.</summary>
    public int MatchupGames { get; init; }

    /// <summary>
    /// The expected matchup before the lane opponent is known: the deltas into
    /// the opponents still available, weighted by how often each is played on the
    /// lane, with how many of the most-played ones the pick is clearly behind
    /// into (#1906).
    /// </summary>
    public DraftBlindReadModel Blind { get; init; } = new();

    /// <summary>
    /// The champion's win rate at the lane minus the lane's average win rate —
    /// how strong the pick is on its own, before any matchup.
    /// </summary>
    public double StrengthDelta { get; init; }

    /// <summary>Games the strength rests on: the champion's games at the lane.</summary>
    public int StrengthGames { get; init; }

    /// <summary>
    /// Mean observed-minus-expected win rate with the allies on the board (locked,
    /// and hovered at half weight), straight from the site's synergy figures —
    /// a mean since #1906, so four allies do not outweigh one lane.
    /// </summary>
    public double SynergyDelta { get; init; }

    /// <summary>Games the synergy rests on, across every measured ally.</summary>
    public int SynergyGames { get; init; }

    /// <summary>
    /// The ranking key: the lane term (the opponent where the board shows one,
    /// the blind expectation for the rest), the champion's strength at the lane,
    /// then the ally term, each delta shrunk
    /// towards zero by its games. Deliberately not called a win probability, and
    /// not meant to be displayed.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// True when the lane reading in play and the synergy both rest on fewer
    /// games than their floor. A display flag: the shrinkage already orders thin
    /// candidates.
    /// </summary>
    public bool ThinSample { get; init; }

    /// <summary>
    /// The two or three facts that carry the pick, strongest first, as data: the
    /// client writes the sentence, the numbers stay the endpoint's.
    /// </summary>
    public IReadOnlyList<DraftReasonReadModel> Reasons { get; init; } = [];

    /// <summary>
    /// The patch the matchup figures were read from — the current one, or
    /// <c>current+previous</c> when the current patch was too thin for this champion.
    /// </summary>
    public string? Patch { get; init; }
}

/// <summary>Blind-pick safety: the expected matchup over the lane opponents still available.</summary>
public sealed record DraftBlindReadModel
{
    /// <summary>Expected matchup delta over the available opponents we have games on, weighted by play rate on the lane.</summary>
    public double Delta { get; init; }

    public int Games { get; init; }

    /// <summary>Of <see cref="LikelyOpponents"/>, how many the pick is clearly behind into.</summary>
    public int LosingInto { get; init; }

    /// <summary>How many of the lane's most-played available opponents <see cref="LosingInto"/> counts over.</summary>
    public int LikelyOpponents { get; init; }
}

/// <summary>What kind of fact a <see cref="DraftReasonReadModel"/> states.</summary>
public static class DraftReasonKinds
{
    /// <summary>The matchup into one enemy: <c>championId</c> (the enemy), <c>delta</c>, <c>games</c>, <c>probability</c> it is our opponent.</summary>
    public const string LaneMatchup = "laneMatchup";

    /// <summary>The lane phase into one enemy: <c>rate</c> of decided lanes won at 15 minutes over <c>games</c> decided lanes.</summary>
    public const string LanePhase = "lanePhase";

    /// <summary>Blind safety: <c>delta</c>, <c>games</c>, behind into <c>count</c> of the <c>of</c> most-played opponents.</summary>
    public const string BlindSafety = "blindSafety";

    /// <summary>The champion's own record at the lane: <c>delta</c> against the lane's average, over <c>games</c>.</summary>
    public const string LaneStrength = "laneStrength";

    /// <summary>The pairing with one ally: <c>championId</c>, <c>position</c>, <c>delta</c>, <c>games</c>, <c>tentative</c> when only hovered.</summary>
    public const string Synergy = "synergy";

    /// <summary>A ban: our pick (<c>championId</c>) is behind by <c>delta</c> over <c>games</c> into it, played in <c>share</c> of the lane's games.</summary>
    public const string LaneThreat = "laneThreat";

    /// <summary>A ban with no pick to protect: banned in <c>rate</c> of <c>games</c> matches.</summary>
    public const string BanRate = "banRate";
}

/// <summary>
/// One measured fact behind a suggestion. Text-free on purpose: the fields a
/// kind uses are listed on <see cref="DraftReasonKinds"/>, the others are null.
/// </summary>
public sealed record DraftReasonReadModel
{
    public string Kind { get; init; } = string.Empty;

    public int? ChampionId { get; init; }

    public string? Position { get; init; }

    /// <summary>A win-rate difference in probability points (0.031 = +3.1 pts).</summary>
    public double? Delta { get; init; }

    public int Games { get; init; }

    /// <summary>A plain rate (0..1) — a lane-phase win rate, a ban rate.</summary>
    public double? Rate { get; init; }

    /// <summary>The solver's probability that an enemy is our lane opponent — a lane guess, not a chance to win.</summary>
    public double? Probability { get; init; }

    /// <summary>Share of the lane's games a champion is played in.</summary>
    public double? Share { get; init; }

    public int? Count { get; init; }

    public int? Of { get; init; }

    /// <summary>True when the fact rests on an ally's hover rather than a lock.</summary>
    public bool Tentative { get; init; }
}

/// <summary>
/// The whole answer to one champion-select state, in one payload.
/// </summary>
public sealed record DraftRecommendationResponse
{
    /// <summary>The player's own lane.</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>The current patch the answer reads.</summary>
    public string? Patch { get; init; }

    /// <summary>
    /// The patch added when the current one is too thin for a champion (#1906);
    /// each candidate's <c>patch</c> says whether it was.
    /// </summary>
    public string? PreviousPatch { get; init; }

    public string EloBracket { get; init; } = string.Empty;

    /// <summary>The enemy side as the guesser placed it.</summary>
    public IReadOnlyList<DraftEnemyLaneReadModel> EnemyLanes { get; init; } = [];

    /// <summary>
    /// The enemy the guesser put in our lane, or null while no enemy has been
    /// placed there yet. Null is a normal early-draft state, not an error.
    /// </summary>
    public int? LaneOpponentChampionId { get; init; }

    /// <summary>
    /// How sure we are about the lane opponent specifically — the one number
    /// the whole panel hangs on.
    /// </summary>
    public double LaneOpponentConfidence { get; init; }

    /// <summary>
    /// The solver's probability that our lane opponent is already on the board,
    /// over every placement it weighs (#1906). Below 1, the blind figures carry the
    /// rest of the lane term.
    /// </summary>
    public double LaneOpponentProbability { get; init; }

    /// <summary>Candidates, best first.</summary>
    public IReadOnlyList<DraftCandidateReadModel> Candidates { get; init; } = [];
}
