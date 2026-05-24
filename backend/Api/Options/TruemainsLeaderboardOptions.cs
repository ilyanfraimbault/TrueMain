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
    /// Minimum number of ranked solo games (QueueId=420) an account must
    /// have on file to appear on the leaderboard. Filters out one-off
    /// accounts and noise from rank snapshots that don't reflect real
    /// activity. Set to 0 to disable the filter (used in integration tests
    /// that don't seed match_participants).
    /// </summary>
    public int MinRankedGames { get; set; } = 20;
}
