namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Result of the composition match search: the top-K most similar games plus
/// the confidence signals the recommendation layer surfaces — how many games
/// were scanned, how close the selection got to the requested draft. Sparse
/// data must read as sparse, never as fabricated certainty.
/// </summary>
public sealed class CompositionMatchesResult
{
    public required int ChampionId { get; init; }

    public required string Position { get; init; }

    /// <summary>Normalised patch filter applied, null when unfiltered.</summary>
    public string? Patch { get; init; }

    /// <summary>
    /// Candidate games actually scanned (bounded by the configured pool cap).
    /// </summary>
    public required int CandidatePoolSize { get; init; }

    /// <summary>
    /// How many of the selected games were piloted by a main of the champion.
    /// Games by mains are preferred over incidental games, so this is the share
    /// of the build that comes from dedicated players.
    /// </summary>
    public required int TruemainGameCount { get; init; }

    /// <summary>
    /// Score a game reproducing every requested slot would reach; zero when
    /// the request carried no composition slots.
    /// </summary>
    public required int MaxPossibleScore { get; init; }

    /// <summary>
    /// Mean of <c>Score / MaxPossibleScore</c> over the selected games, 0 when
    /// no slot was requested or nothing was selected.
    /// </summary>
    public required double MeanSimilarity { get; init; }

    /// <summary>
    /// True when the request pinned the role opponent (an enemy at the
    /// player's own position). The matchup is then a hard requirement, not a
    /// ranking signal: only games with that exact matchup are selectable.
    /// </summary>
    public required bool MatchupRequested { get; init; }

    /// <summary>
    /// False only when the role opponent was requested and no scanned game
    /// contains that matchup — <see cref="Matches"/> is then empty and the
    /// caller should fall back to the champion's baseline build.
    /// </summary>
    public required bool MatchupFound { get; init; }

    /// <summary>
    /// Selected games. With a pinned role opponent that is every game of the matchup the
    /// retained window holds, newest first, bounded by the pool cap; without one, the
    /// most similar games of the champion at the position — mains first, then best score,
    /// recency breaking ties.
    /// </summary>
    public required IReadOnlyList<CompositionMatchRef> Matches { get; init; }
}

/// <summary>
/// One selected game: the keys the aggregation step (#559) needs to load the
/// participant's build, plus its similarity weight, outcome, and who piloted
/// it — the provenance drawer (#940) lists the selection back to the user, so
/// the pilot and the main flag have to survive the selection, not be
/// re-derived from it.
/// </summary>
public sealed class CompositionMatchRef
{
    public required string MatchId { get; init; }

    public required int ParticipantId { get; init; }

    public required int Score { get; init; }

    public required bool Win { get; init; }

    public required DateTime GameStartTimeUtc { get; init; }

    /// <summary>Puuid of the player who piloted the champion in that game.</summary>
    public required string Puuid { get; init; }

    /// <summary>
    /// True when <see cref="Puuid"/> is an active main of the champion. Worth a vote
    /// multiplier in the aggregation, and the first selection tier when the pool is
    /// capped (see <c>CompositionMatchQueryService</c>).
    /// </summary>
    public required bool IsTruemain { get; init; }

    /// <summary>
    /// True when the game was played on the patch the recommendation is for (#1659).
    /// Carries the patch vote multiplier without making the aggregation re-derive a
    /// patch from a game version.
    /// </summary>
    public required bool IsCurrentPatch { get; init; }
}
