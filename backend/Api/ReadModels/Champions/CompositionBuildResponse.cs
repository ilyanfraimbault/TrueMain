namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Response of <c>POST /champions/{id}/composition-build</c>: the win-weighted
/// build recommendation for the requested draft plus the confidence signals
/// the frontend surfaces — how much data backs it and how close the sample
/// actually got to the requested composition. Sparse data must read as
/// sparse, never as fabricated certainty.
/// </summary>
public sealed record CompositionBuildResponse
{
    public required int ChampionId { get; init; }

    public required string Position { get; init; }

    /// <summary>Normalised patch filter applied, null when unfiltered.</summary>
    public string? Patch { get; init; }

    /// <summary>Resolved elo filter token (<c>ALL</c> when unfiltered).</summary>
    public required string EloBracket { get; init; }

    /// <summary>
    /// True when the draft pinned the lane opponent — the matchup is then a
    /// hard requirement on the sampled games, not a ranking signal.
    /// </summary>
    public required bool MatchupRequested { get; init; }

    /// <summary>
    /// False only when the lane opponent was requested and no recorded game
    /// has that matchup — the build is then empty and the client should fall
    /// back to the champion's baseline build.
    /// </summary>
    public required bool MatchupFound { get; init; }

    public required CompositionConfidenceReadModel Confidence { get; init; }

    public required CompositionBuildRecommendation Build { get; init; }
}

/// <summary>
/// Confidence signals of one recommendation: the sample that was aggregated,
/// the pool it was drawn from, and how similar the sample is to the requested
/// draft (0 when no composition slot was provided — the recommendation then
/// degrades to the champion's most recent games at the position).
/// </summary>
public sealed record CompositionConfidenceReadModel
{
    /// <summary>Games actually aggregated (the selected top-K size).</summary>
    public required int SampleSize { get; init; }

    /// <summary>Candidate games scanned (bounded by the configured pool cap).</summary>
    public required int CandidatePoolSize { get; init; }

    /// <summary>Score a game reproducing every requested slot would reach.</summary>
    public required int MaxPossibleScore { get; init; }

    /// <summary>Mean of score/max over the selected games, in [0, 1].</summary>
    public required double MeanSimilarity { get; init; }
}
