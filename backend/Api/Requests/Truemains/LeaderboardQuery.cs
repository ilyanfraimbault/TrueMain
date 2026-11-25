using System.ComponentModel.DataAnnotations;
using TrueMain.Services.Truemains.Leaderboard;

namespace TrueMain.Requests.Truemains;

/// <summary>
/// Query parameters for <c>GET /truemains</c>. <see cref="Page"/> and
/// <see cref="PageSize"/> carry <see cref="RangeAttribute"/>s so
/// <c>[ApiController]</c> rejects an out-of-range value with a 400
/// ProblemDetails at binding time, instead of the service silently clamping
/// it.
/// </summary>
public sealed record LeaderboardQuery
{
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, 50)]
    public int? PageSize { get; init; }

    public string? Region { get; init; }

    public string? Position { get; init; }

    public int? ChampionId { get; init; }

    public bool? OtpOnly { get; init; }

    /// <summary>
    /// Ranking column, always descending: <c>dedication</c> ranks by TrueMain's
    /// dedication score, <c>games</c> / <c>kda</c> / <c>winRate</c> by the
    /// row's Games / KDA / WR figure (case-insensitive); anything else
    /// (including omitted) keeps the default ranked-standing order.
    /// </summary>
    public string? Sort { get; init; }

    internal LeaderboardSort ParsedSort => Sort?.Trim() switch
    {
        { } value when value.Equals("dedication", StringComparison.OrdinalIgnoreCase) => LeaderboardSort.Dedication,
        { } value when value.Equals("games", StringComparison.OrdinalIgnoreCase) => LeaderboardSort.Games,
        { } value when value.Equals("kda", StringComparison.OrdinalIgnoreCase) => LeaderboardSort.Kda,
        { } value when value.Equals("winRate", StringComparison.OrdinalIgnoreCase) => LeaderboardSort.WinRate,
        _ => LeaderboardSort.Rank,
    };
}
