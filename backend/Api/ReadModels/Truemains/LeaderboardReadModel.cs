namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// One row of the truemains leaderboard (<c>GET /truemains</c>). Identity comes
/// from <c>riot_accounts</c>, ranked from the latest <c>rank_snapshots</c> per
/// account, stats from a participant aggregate on the same page slice, and top
/// champions from <c>main_champion_stats</c> filtered to <c>IsMain=true</c>.
/// </summary>
public sealed record LeaderboardRowReadModel
{
    /// <summary>1-based position on the filtered leaderboard, computed server-side so the UI doesn't need to track <c>(page-1)*pageSize</c>.</summary>
    public int Rank { get; init; }

    public ProfileIdentityReadModel Identity { get; init; } = new();

    /// <summary>One of <c>europe</c>, <c>americas</c>, <c>korea</c>. Rows from platforms outside those buckets never reach the leaderboard.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Null when the account has no rank snapshot yet — those rows sort last via NULLS LAST.</summary>
    public LeaderboardRankedReadModel? Ranked { get; init; }

    public LeaderboardStatsReadModel Stats { get; init; } = new();

    /// <summary>Up to 3 most-played champions, ordered by descending games. Empty when no main-champion analysis has run.</summary>
    public IReadOnlyList<LeaderboardTopChampionReadModel> TopChampions { get; init; }
        = Array.Empty<LeaderboardTopChampionReadModel>();
}

public sealed record LeaderboardRankedReadModel
{
    public string Tier { get; init; } = string.Empty;
    public string Division { get; init; } = string.Empty;
    public int LeaguePoints { get; init; }

    /// <summary>The exact value used for ORDER BY in SQL — exposed so the UI can show the sort key alongside the raw tier/LP.</summary>
    public int Score { get; init; }
}

public sealed record LeaderboardStatsReadModel
{
    public int Games { get; init; }

    /// <summary>Wins across the visible games window (currently lifetime ranked). Null when no participant rows attributed to the account.</summary>
    public int? Wins { get; init; }

    public int? Losses { get; init; }

    /// <summary><c>wins / (wins + losses)</c> when both are known, otherwise null — the frontend hides the cell instead of rendering 0% / NaN.</summary>
    public double? WinRate { get; init; }

    /// <summary><c>(kills + assists) / max(1, deaths)</c> across attributed participant rows, null when none.</summary>
    public double? Kda { get; init; }
}

public sealed record LeaderboardTopChampionReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    /// <summary>The player's authoritative play rate for this champion (0..1), as stored by main analysis (<c>games / total games on the account</c>).</summary>
    public double PlayRate { get; init; }

    /// <summary>Keystone rune of the player's dominant build for this champion. Null when no aggregated build exists for the player on this champion.</summary>
    public int? PrimaryKeystoneId { get; init; }

    /// <summary>Secondary rune tree of the player's dominant build for this champion. Null when no aggregated build exists.</summary>
    public int? SecondaryStyleId { get; init; }

    /// <summary>First completed item of the player's dominant build for this champion. Null when no aggregated build exists.</summary>
    public int? FirstItemId { get; init; }
}

/// <summary>
/// Paged response shape for <c>GET /truemains</c>. Mirrors
/// <see cref="MatchSummariesResponse"/> so the frontend's pagination controls
/// behave identically across leaderboard and match-history pages.
/// </summary>
public sealed record LeaderboardResponse
{
    public IReadOnlyList<LeaderboardRowReadModel> Rows { get; init; }
        = Array.Empty<LeaderboardRowReadModel>();

    /// <summary>1-indexed current page.</summary>
    public int Page { get; init; }

    /// <summary>Number of rows per page (the value the service clamped to).</summary>
    public int PageSize { get; init; }

    /// <summary>Total rows across all pages for the active filter — drives the page-count UI.</summary>
    public int Total { get; init; }
}
