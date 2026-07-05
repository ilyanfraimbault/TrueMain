namespace TrueMain.Services.Truemains;

/// <summary>
/// Shared rules for how a player's "mains" are surfaced across the truemain
/// views. Kept in one place so the profile and the leaderboard can never drift
/// apart on how many champions define a player — a divergence there would show a
/// different primary/secondary lane for the same player on two pages (#521).
/// </summary>
internal static class MainChampionsPolicy
{
    /// <summary>
    /// The most-played mains a player is represented by (ordered by PlayRate
    /// desc, then ChampionMatches desc). MainStatsCalculator can flag more than
    /// this many champions <c>IsMain</c> when coverage is thin
    /// (CriticalPlayRateThreshold drops to 0.1), so both the profile
    /// (ProfileQueryService.FetchMainsAsync) and the leaderboard
    /// (TruemainsLeaderboardQueryService.FetchPositionsAsync) cap to this slice.
    /// </summary>
    public const int Cap = 6;
}
