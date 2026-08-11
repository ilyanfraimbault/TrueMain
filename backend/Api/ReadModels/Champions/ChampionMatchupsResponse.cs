namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Lane-matchups read model returned by the champion matchup endpoints. Lists
/// how a champion performed at a position against every lane opponent (same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>) it met over the scoped games.
///
/// Served from the pre-aggregated <c>champion_matchup_stats</c> (#606) for every
/// global slice — leaderboard and single-opponent search alike. Only the
/// player-scoped route is computed live from <c>match_participants</c>, because
/// the aggregate carries no account dimension. (The rows are stored floor-free,
/// so the search reading them keeps its "answer from one game up" contract; the
/// earlier claim that an aggregate "built at floor 10" could not serve it was
/// never true of the storage, only of the read.)
///
/// The leaderboard drops opponents below the larger of
/// <see cref="TrueMain.Options.ChampionsListOptions.MinMatchupGames"/> and
/// <see cref="TrueMain.Options.ChampionsListOptions.MinMatchupPlayRate"/> × the
/// champion's total matchup games in the same scope; the search applies neither.
///
/// <see cref="Matchups"/> is ordered by <see cref="ChampionMatchupEntry.WinRate"/>
/// descending, but the best / worst slicing is *not* that order — see
/// <see cref="ChampionMatchupEntry.WinRateLowerBound"/>.
/// </summary>
public sealed record ChampionMatchupsResponse
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// Resolved patch the slice was computed for (<c>major.minor</c>), or
    /// <see langword="null"/> when the caller did not pin a patch and the
    /// slice spans every patch with data.
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// One entry per lane opponent above the minimum-games floor, ordered by
    /// <see cref="ChampionMatchupEntry.WinRate"/> descending.
    /// </summary>
    public IReadOnlyList<ChampionMatchupEntry> Matchups { get; init; } = [];
}

/// <summary>
/// A single lane opponent's head-to-head line: how the champion fared against
/// this opponent across the scoped games.
/// </summary>
public sealed record ChampionMatchupEntry
{
    public int OpponentChampionId { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    /// <summary>
    /// <see cref="Wins"/> / <see cref="Games"/>. <see cref="Games"/> is always
    /// at least the minimum-games floor here, so this never divides by zero.
    /// </summary>
    public double WinRate { get; init; }

    /// <summary>
    /// Share this opponent holds of the champion's total matchup games in the same
    /// scope — <see cref="Games"/> over the games summed across every opponent, before
    /// any floor. The quantity the leaderboard's floor is expressed in
    /// (<see cref="TrueMain.Options.ChampionsListOptions.MinMatchupPlayRate"/>), returned
    /// so a client can say "you meet this opponent in 4% of your games" rather than
    /// leaving a bare game count to mean whatever the reader assumes. The
    /// single-opponent search reads one row, so it takes its denominator from a second
    /// aggregate over the same scope rather than from the row itself.
    ///
    /// Zero only on the player-scoped route, whose live join is narrowed to the one
    /// opponent and holds no such total.
    /// </summary>
    public double PlayRate { get; init; }

    /// <summary>
    /// Lower bound of the 95% Wilson interval around <see cref="WinRate"/> — "at worst,
    /// this matchup is this good". <b>This is what the best-matchups list is ranked
    /// by</b>, not <see cref="WinRate"/>: ranking the raw rate makes the leaderboard a
    /// small-sample detector, because on a wide enough field the most extreme rate is
    /// always the thinnest sample. A 62% over 96 games bounds at 53%, a 53% over 995
    /// games bounds at 50%, and a 82% over 11 games bounds at 52% — which is the whole
    /// point, since eleven games cannot establish an 82% matchup.
    /// </summary>
    public double WinRateLowerBound { get; init; }

    /// <summary>
    /// Upper bound of the same interval — "at best, this matchup is only this good" —
    /// and the key the *worst*-matchups list is ranked by, ascending. Deliberately not
    /// the mirror of the lower bound: a bad matchup is one whose ceiling is low, and
    /// using the lower bound at both ends would rank the worst list by which sample is
    /// thinnest, reintroducing at the bottom exactly what the top was fixed for.
    /// </summary>
    public double WinRateUpperBound { get; init; }

    /// <summary>
    /// Share of *decided* lanes the champion won against this opponent — ahead by
    /// more than the configured gold threshold at 15 minutes (#919).
    ///
    /// <para>
    /// The denominator is <see cref="DecidedLaneGames"/>, not <see cref="Games"/>: a
    /// match with no ingested timeline, or one that ended before 15 minutes, cannot
    /// be judged, and a lane inside the threshold band was neither won nor lost.
    /// Dividing by games played would understate every figure here.
    /// </para>
    ///
    /// <para>
    /// <see langword="null"/> when nothing can be said: fewer decided lanes than
    /// <see cref="TrueMain.Options.ChampionsListOptions.MinDecidedLaneGames"/> (which
    /// includes none at all), or a <em>player-scoped</em> slice, whose lane cannot be
    /// read off a population-wide aggregate. Every global slice carries it, search
    /// included — both halves of that row now come from the same aggregate rows, so
    /// the lane sample can no longer exceed the games it sits beside. Never a
    /// substitute zero.
    /// </para>
    /// </summary>
    public double? LaneWinRate { get; init; }

    /// <summary>
    /// Lanes actually decided — won or lost past the threshold — and so the sample
    /// <see cref="LaneWinRate"/> rests on. Always smaller than <see cref="Games"/>,
    /// often much smaller early on, which is why it is returned rather than left for
    /// the client to guess.
    /// </summary>
    public int DecidedLaneGames { get; init; }

    /// <summary>
    /// Mean gold gap over the lane opponent at 15 minutes, in gold, signed from the
    /// champion's point of view (#976). This is the *magnitude*
    /// <see cref="LaneWinRate"/> cannot carry: a lane won 60% of the time by 120 gold
    /// and one won 60% of the time by 1200 are the same rate and a different matchup.
    ///
    /// <para>
    /// Averaged over <see cref="GoldDiffLaneGames"/> — the lanes the gap was actually
    /// summed over, which for rows folded before #976 is fewer than
    /// <see cref="DecidedLaneGames"/> and often zero. <see langword="null"/> then,
    /// never 0: "we have not measured this gap" and "the lane is dead even" are the
    /// opposite of interchangeable here, since 0 is the most decisive-looking value
    /// the number can take.
    /// </para>
    /// </summary>
    public double? AverageGoldDiffAt15 { get; init; }

    /// <summary>
    /// Lanes <see cref="AverageGoldDiffAt15"/> is averaged over. Returned so the client
    /// can withhold a verdict on a sample too small to band, rather than turning three
    /// lanes into a confident label.
    /// </summary>
    public int GoldDiffLaneGames { get; init; }
}
