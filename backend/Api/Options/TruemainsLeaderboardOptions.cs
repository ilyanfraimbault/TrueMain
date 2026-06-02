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
    /// Minimum number of valid-position ranked-solo games in its main-analysis
    /// window an account must have to appear on the leaderboard. Filters out
    /// one-off accounts and rank-snapshot noise that doesn't reflect real
    /// activity. Compared against <c>main_champion_stats.TotalMatches</c>, which
    /// main analysis (MainStatsCalculator) computes over queue-420 games that
    /// carry a parseable team position — games with NONE/UNKNOWN position are
    /// excluded, so this floor is intentionally measured on positioned games
    /// the analysis actually considered, not every ranked row on file.
    /// TotalMatches saturates at <c>MainAnalysis.MatchesToConsider</c>, so the
    /// value is cross-validated on start against that option (see Program.cs):
    /// above the cap it fails fast at boot instead of silently emptying the
    /// leaderboard (the <c>TotalMatches</c> predicate could never be satisfied).
    /// Because main analysis only sets <c>IsMain = true</c> when
    /// <c>TotalMatches &gt;= MainAnalysis.MinMatchesToEvaluate</c>, any value at
    /// or below that threshold is a no-op. Set to 0 to disable.
    /// </summary>
    public int MinRankedGames { get; set; } = 20;
}
