namespace Core.Lol.Pace;

/// <summary>
/// A cumulative per-minute value the pace benchmark (#1912) keeps a histogram of. Both are
/// read off a match-v5 timeline frame; the desktop overlay divides them by the minute to
/// show a per-minute rate.
/// </summary>
public enum PaceMetric
{
    /// <summary>Minions plus jungle monsters killed (<c>minionsKilled + jungleMinionsKilled</c>).</summary>
    Cs = 0,

    /// <summary>Gold earned so far, starting gold included (the frame's <c>totalGold</c>).</summary>
    GoldEarned = 1,
}
