namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Lane-matchups read model returned by the champion matchup endpoints. Lists
/// how a champion performed at a position against every lane opponent (same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>) it met over the scoped games.
/// Computed live from <c>match_participants</c> — there is no aggregation table
/// behind it.
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
}
