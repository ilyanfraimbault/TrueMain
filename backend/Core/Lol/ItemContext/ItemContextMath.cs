namespace Core.Lol.ItemContext;

/// <summary>
/// The statistics behind a situational verdict (#1450): is an item's pick rate genuinely
/// different at the two ends of a draft axis, or is the gap what a sample this size
/// produces on its own?
/// </summary>
/// <remarks>
/// A two-proportion z-test rather than the Wilson bound the matchup leaderboard ranks on
/// (<c>Api/Services/RateMath.cs</c>): that one asks "how good is this rate at worst",
/// which is a ranking question over one sample, while this one asks "are these two rates
/// different", which is a comparison between two. Both floors are applied together — a
/// gap can be significant and tiny at once, and a tiny gap is not an explanation, so the
/// caller also holds an absolute-lift floor.
/// </remarks>
public static class ItemContextMath
{
    /// <summary>
    /// Two-sided z for the difference between two independent proportions, under the null
    /// hypothesis that both come from the same rate (hence the pooled estimate). Returns
    /// 0 when either side is empty or the pooled rate is degenerate — no sample, no
    /// evidence.
    /// </summary>
    public static double TwoProportionZ(int successesA, int totalA, int successesB, int totalB)
    {
        if (totalA <= 0 || totalB <= 0)
        {
            return 0d;
        }

        var rateA = successesA / (double)totalA;
        var rateB = successesB / (double)totalB;
        var pooled = (successesA + successesB) / (double)(totalA + totalB);

        // 0 and 1 both give a zero standard error: every game agreed, so there is nothing
        // left for the test to measure and the absolute-lift floor is the whole judgement.
        if (pooled is <= 0d or >= 1d)
        {
            return 0d;
        }

        var standardError = Math.Sqrt(pooled * (1d - pooled) * ((1d / totalA) + (1d / totalB)));
        return standardError <= 0d ? 0d : (rateA - rateB) / standardError;
    }

    /// <summary>
    /// Whether the two rates differ beyond <paramref name="minAbsoluteZ"/> — the |z| the
    /// caller configures, 1.96 being the 95% two-sided edge.
    /// </summary>
    public static bool IsSignificant(int successesA, int totalA, int successesB, int totalB, double minAbsoluteZ)
        => Math.Abs(TwoProportionZ(successesA, totalA, successesB, totalB)) >= minAbsoluteZ;
}
