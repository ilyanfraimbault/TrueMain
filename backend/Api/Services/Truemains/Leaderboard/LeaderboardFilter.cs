namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// Every input that decides <em>which accounts</em> the leaderboard shows and
/// in what order they rank: the eligibility SQL, the dedication ranking and
/// both cache keys are all built on this one shape.
/// </summary>
/// <param name="Platforms">Platforms the leaderboard surfaces for this request, never empty.</param>
/// <param name="ChampionId">Restricts to mains of one champion, or null for any champion.</param>
/// <param name="Position">Normalised position (TOP/JUNGLE/...), or null for any position.</param>
/// <param name="MinGames">Ranked-games floor on the main row.</param>
/// <param name="OtpOnly">Restricts to one-trick-pony mains.</param>
internal sealed record LeaderboardFilter(
    string[] Platforms,
    int? ChampionId,
    string? Position,
    int MinGames,
    bool OtpOnly)
{
    /// <summary>
    /// Cache-key fragment for this filter shape. The per-page response cache
    /// and the dedication ranking cache both embed it, so they can never
    /// disagree about what a "filter shape" is — adding a filter here covers
    /// both by construction.
    /// </summary>
    public string Key
    {
        get
        {
            // Caller-stable: platforms are normalised upstream (RegionFilterParser
            // returns a deterministic iteration), but sorting defends against
            // future drift if another caller passes them in a different order.
            // The "_" sentinel keeps nullable values distinct from any literal
            // filter value that could collide on the key (no champion uses "_"
            // as an ID, no position is "_").
            var platformPart = string.Join(",", Platforms.OrderBy(p => p, StringComparer.Ordinal));
            var championPart = ChampionId?.ToString() ?? "_";
            var positionPart = Position ?? "_";
            var otpPart = OtpOnly ? "otp" : "_";
            return $"{platformPart}:{championPart}:{positionPart}:{MinGames}:{otpPart}";
        }
    }
}
