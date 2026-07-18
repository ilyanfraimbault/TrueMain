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
    /// True when the request pinned the lane opponent (an enemy at the
    /// player's own position). The matchup is then a hard requirement, not a
    /// ranking signal: only games with that exact matchup are selectable.
    /// </summary>
    public required bool MatchupRequested { get; init; }

    /// <summary>
    /// False only when the lane opponent was requested and no scanned game
    /// contains that matchup — <see cref="Matches"/> is then empty and the
    /// caller should fall back to the champion's baseline build.
    /// </summary>
    public required bool MatchupFound { get; init; }

    /// <summary>Selected games, best score first (recency breaks ties).</summary>
    public required IReadOnlyList<CompositionMatchRef> Matches { get; init; }
}

/// <summary>
/// One selected game: the keys the aggregation step (#559) needs to load the
/// participant's build, plus its similarity weight and outcome.
/// </summary>
public sealed class CompositionMatchRef
{
    public required string MatchId { get; init; }

    public required int ParticipantId { get; init; }

    public required int Score { get; init; }

    public required bool Win { get; init; }

    public required DateTime GameStartTimeUtc { get; init; }
}
