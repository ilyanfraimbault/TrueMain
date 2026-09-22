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
/// The two components stay separate all the way to the client on purpose: the
/// panel has to be able to say a pick is good *because the lane is favourable*
/// or *because it fits the team*, which a single total cannot express.
/// </summary>
public sealed record DraftCandidateReadModel
{
    public int ChampionId { get; init; }

    /// <summary>
    /// Win rate against the resolved lane opponent minus this champion's win
    /// rate at this lane overall. A measured difference, not a model output:
    /// both halves come from <c>champion_matchup_stats</c>.
    /// </summary>
    public double MatchupDelta { get; init; }

    /// <summary>Games the matchup half rests on. Zero means the pairing is unseen.</summary>
    public int MatchupGames { get; init; }

    /// <summary>
    /// Summed observed-minus-expected win rate with the allies already locked,
    /// straight from the site's synergy figures.
    /// </summary>
    public double SynergyDelta { get; init; }

    /// <summary>Games the synergy half rests on, across every locked ally.</summary>
    public int SynergyGames { get; init; }

    /// <summary>
    /// <see cref="MatchupDelta"/> + <see cref="SynergyDelta"/>. Deliberately not
    /// called a win probability: it is the sum of two measured deltas and says
    /// nothing about the rest of the draft.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// True when neither half clears its games floor. The candidate is still
    /// returned — with its numbers — so the client can show it as unproven
    /// rather than silently dropping a pick the player owns.
    /// </summary>
    public bool ThinSample { get; init; }
}

/// <summary>
/// The whole answer to one champion-select state, in one payload.
/// </summary>
public sealed record DraftRecommendationResponse
{
    /// <summary>The player's own lane.</summary>
    public string Position { get; init; } = string.Empty;

    public string? Patch { get; init; }

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

    /// <summary>Candidates, best first.</summary>
    public IReadOnlyList<DraftCandidateReadModel> Candidates { get; init; } = [];
}
