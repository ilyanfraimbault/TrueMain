namespace TrueMain.Options;

/// <summary>
/// Quality thresholds for the <c>GET /truemains</c> leaderboard. These are
/// product knobs the user wants to tweak without a redeploy, so they bind
/// from <c>TruemainsLeaderboard:*</c> in configuration.
/// </summary>
public sealed class TruemainsLeaderboardOptions
{
    public const string SectionName = "TruemainsLeaderboard";

    /// <summary>
    /// Minimum number of ranked solo games (QueueId=420) an account must have
    /// in its main-analysis window to appear on the leaderboard. Filters out
    /// one-off accounts and rank-snapshot noise that doesn't reflect real
    /// activity. Compared against <c>main_champion_stats.TotalMatches</c>,
    /// which is capped at <c>MainAnalysis.MatchesToConsider</c> (50) — so keep
    /// this at or below that cap. Because main analysis only sets
    /// <c>IsMain = true</c> when <c>TotalMatches &gt;= MainAnalysis.MinMatchesToEvaluate</c>,
    /// any value at or below that threshold is a no-op. Set to 0 to disable.
    /// </summary>
    public int MinRankedGames { get; set; } = 20;
}
