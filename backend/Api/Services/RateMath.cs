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
}
