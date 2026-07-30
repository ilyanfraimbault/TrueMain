namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Lane-matchups read model returned by the champion matchup endpoints. Lists
/// how a champion performed at a position against every lane opponent (same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>) it met over the scoped games.
///
/// Served from the pre-aggregated <c>champion_matchup_stats</c> (#606) for the
/// panel, and computed live from <c>match_participants</c> only for the
/// single-opponent search, whose floor of 1 game an aggregate built at floor 10
/// cannot answer. (The previous version of this comment claimed there was no
/// aggregation table at all, which stopped being true with #606.)
///
/// Only opponents with at least the configured minimum games
/// (<see cref="TrueMain.Options.ChampionsListOptions.MinMatchupGames"/>) appear;
/// thinner head-to-heads are noise. <see cref="Matchups"/> is ordered by
/// <see cref="ChampionMatchupEntry.WinRate"/> descending so a caller slicing the
/// best / worst opponents gets a stable list, but the frontend derives nothing
/// else from the order.
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
    /// <see langword="null"/> when nothing can be said: no decided lane in the scope,
    /// or the single-opponent search path, which is a live query over participants
    /// with no lane data behind it. Never a substitute zero.
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
}
