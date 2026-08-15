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
    /// True when the draft pinned the role opponent — the matchup is then a
    /// hard requirement on the sampled games, not a ranking signal.
    /// </summary>
    public required bool MatchupRequested { get; init; }

    /// <summary>
    /// False only when the role opponent was requested and no recorded game
    /// has that matchup — the build is then empty and the client should fall
    /// back to the champion's baseline build.
    /// </summary>
    public required bool MatchupFound { get; init; }

    public required CompositionConfidenceReadModel Confidence { get; init; }

    /// <summary>
    /// How the lane went in the sampled games (#1117) — measured over exactly the games
    /// <see cref="Confidence"/> counts, so the tool's stat line describes one population
    /// throughout. It used to read the matchup aggregate instead, whose champion side is
    /// mains-only (#1087), which showed "—" beside a sample of eight games whenever no
    /// main had played the matchup.
    /// </summary>
    public required CompositionLaneReadModel Lane { get; init; }

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

    /// <summary>
    /// How many of the aggregated games were piloted by a main of the champion
    /// (mains are preferred in selection).
    /// </summary>
    public required int TruemainGameCount { get; init; }

    /// <summary>Score a game reproducing every requested slot would reach.</summary>
    public required int MaxPossibleScore { get; init; }

    /// <summary>Mean of score/max over the selected games, in [0, 1].</summary>
    public required double MeanSimilarity { get; init; }
}

/// <summary>
/// The lane at 15 minutes over a recommendation's own sample. Three denominators
/// because they are three different questions: how many of the sampled games could be
/// judged at all, how many of those were actually decided, and — for the gaps — the
/// judged ones again, evens included.
/// </summary>
public sealed record CompositionLaneReadModel
{
    /// <summary>Nothing sampled, or nothing judgeable in it.</summary>
    public static CompositionLaneReadModel Empty { get; } = new();

    /// <summary>
    /// Sampled games where both lane sides had a 15-minute reading. Smaller than the
    /// sample: a game that ended before the mark, or whose timeline was never ingested,
    /// is a game but not a judgeable lane.
    /// </summary>
    public int MeasuredGames { get; init; }

    /// <summary>
    /// Of <see cref="MeasuredGames"/>, those settled past the gold threshold either
    /// way. Lanes inside the band are decided by neither side and belong to neither —
    /// which is why this is stored rather than derived from the measured count.
    /// </summary>
    public int DecidedGames { get; init; }

    /// <summary>
    /// Share of <see cref="DecidedGames"/> the champion was ahead in.
    /// <see langword="null"/> when none were decided — never 0, which would read as
    /// "the lane is always lost".
    /// </summary>
    public double? WinRate { get; init; }

    /// <summary>
    /// Mean gold gap over the lane opponent at 15 minutes, across
    /// <see cref="MeasuredGames"/> — evens included, since an even lane is exactly what
    /// the counters above cannot express. <see langword="null"/> when nothing was
    /// measured.
    /// </summary>
    public double? AverageGoldDiffAt15 { get; init; }

    /// <summary>
    /// Mean experience gap over the same games. Beside the gold rather than derived
    /// from it: gold is who bought more, XP is who is bigger, and a lane won on kills
    /// while losing waves shows one ahead and the other behind.
    /// </summary>
    public double? AverageXpDiffAt15 { get; init; }
}
