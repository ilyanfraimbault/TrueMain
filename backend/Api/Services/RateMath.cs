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
    /// z for a 95% two-sided interval. Not configurable: it is the confidence the
    /// bounds below *mean*, not a knob — moving it would silently redefine what
    /// every caller's "at worst" reads as.
    /// </summary>
    private const double WilsonZ = 1.959963984540054d;

    /// <summary>
    /// Wilson score interval for a win rate — the range the true rate plausibly sits
    /// in given the sample, at 95% confidence. Returns <c>(0, 1)</c> for an empty
    /// sample: nothing observed constrains nothing.
    /// </summary>
    /// <remarks>
    /// Wilson rather than the textbook normal interval because the samples here are
    /// exactly where the normal approximation breaks: small n, and rates far from
    /// 50%. A 9-1 matchup has a normal interval running past 1.0, which is not a
    /// probability; Wilson stays inside [0, 1] and stays asymmetric, which is the
    /// property that makes the bounds usable as a ranking key.
    ///
    /// <para>
    /// Ranking on the lower bound is what stops a leaderboard sorted on the raw rate
    /// from being a small-sample detector: sorting <c>wins/games</c> descending puts
    /// whichever line has the fewest games on top essentially by construction, since
    /// variance — not skill — produces the biggest numbers. The lower bound asks
    /// "how good is this matchup *at worst*", which a thin sample cannot answer
    /// confidently and therefore cannot win on. Measured on production, it moves a
    /// 53.3% over 995 games ahead of a 57.1% over 182, and both ahead of anything
    /// resting on a dozen.
    /// </para>
    /// </remarks>
    public static (double Lower, double Upper) WilsonInterval(int wins, int games)
    {
        if (games <= 0)
        {
            return (0d, 1d);
        }

        var n = (double)games;
        var p = wins / n;
        var z2 = WilsonZ * WilsonZ;
        var denominator = 1d + z2 / n;
        var centre = (p + z2 / (2d * n)) / denominator;
        var margin = WilsonZ * Math.Sqrt(p * (1d - p) / n + z2 / (4d * n * n)) / denominator;

        return (Math.Max(0d, centre - margin), Math.Min(1d, centre + margin));
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
