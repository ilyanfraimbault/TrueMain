namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Lane-matchup read model returned by the champion matchup endpoints. Reports
/// how a champion performs at a position when it faced a specific opponent in
/// the same lane (same <c>TeamPosition</c>, opposite <c>TeamId</c>) across the
/// scoped games. Computed live from <c>match_participants</c> — there is no
/// aggregation table behind it.
///
/// Only the matchup slice is returned: the frontend already knows the
/// champion's overall win rate (from the champion page) and derives the
/// matchup delta itself, so no overall / delta field lives here.
/// </summary>
public sealed record ChampionMatchupResponse
{
    public int ChampionId { get; init; }

    public int OpponentChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// Resolved patch the slice was computed for (<c>major.minor</c>), or
    /// <see langword="null"/> when the caller did not pin a patch and the
    /// slice spans every patch with data.
    /// </summary>
    public string? Patch { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    /// <summary>
    /// <see cref="Wins"/> / <see cref="Games"/>, or <c>0.0</c> when
    /// <see cref="Games"/> is zero.
    /// </summary>
    public double WinRate { get; init; }
}
