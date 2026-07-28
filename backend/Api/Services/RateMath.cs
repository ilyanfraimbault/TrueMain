namespace TrueMain.Services;

/// <summary>
/// Shared ratio arithmetic for in-memory read-model projections. Not for use
/// inside EF-translated query expressions — those keep the inline form so the
/// division stays in SQL.
/// </summary>
internal static class RateMath
{
    /// <summary>
    /// Share of <paramref name="part"/> in <paramref name="total"/>;
    /// <c>0</c> when the denominator is empty.
    /// </summary>
    public static double Rate(long part, long total)
        => total == 0 ? 0d : (double)part / total;

    /// <summary>
    /// Win rate from nullable win / loss counters (rank snapshots can lack
    /// them); <see langword="null"/> when either counter is unknown or no
    /// games were played.
    /// </summary>
    public static double? WinRate(int? wins, int? losses)
    {
        if (wins is null || losses is null)
        {
            return null;
        }

        var total = wins.Value + losses.Value;
        return total == 0 ? null : (double)wins.Value / total;
    }

    /// <summary>
    /// KDA over an aggregated sample. When the sample recorded no death the
    /// ratio is undefined, so it falls back to <c>(kills + assists) / games</c>
    /// — the KDA the player would have with exactly one death per game.
    /// </summary>
    /// <remarks>
    /// The fallback denominator is <paramref name="games"/>, not 1: every metric
    /// shown next to KDA is a per-game average, so dividing by 1 would print a
    /// career total on a per-game row — a 40-game deathless pool reading "600"
    /// beside "12.4 kills". Dividing by the game count keeps the deathless case
    /// on the same scale as its neighbours and still ranks it above any sample
    /// that did die. Both the champion mains-comparison panel and the truemains
    /// leaderboard call this, so the two surfaces cannot disagree for the same
    /// player (#871).
    /// </remarks>
    public static double Kda(long kills, long deaths, long assists, long games)
    {
        var takedowns = kills + assists;

        if (deaths > 0)
        {
            return (double)takedowns / deaths;
        }

        return games <= 0 ? 0d : (double)takedowns / games;
    }
}
