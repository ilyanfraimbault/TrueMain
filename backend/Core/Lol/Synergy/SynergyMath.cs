namespace Core.Lol.Synergy;

/// <summary>
/// The champion-synergy metric (#922): how much better (or worse) a group of
/// champions does together than the sum of its parts.
///
/// The raw win rate of a pair is NOT the metric. Two strong champions win a lot
/// together simply because they are strong, and ordering by raw win rate just
/// re-ranks the tier list. What a "who should my friend play?" answer needs is the
/// *correlation* — the part of the result the pairing itself explains — so synergy
/// is <c>observed win rate − expected win rate</c>, with the expected value built
/// from each champion's marginal win rate.
///
/// Expected is combined in log-odds space rather than by adding percentages.
/// Averaging (or summing) rates is unbounded — two 70% champions would "expect"
/// 90% — while log-odds addition is the natural scale for combining independent
/// contributions to a binary outcome and can never leave (0, 1).
///
/// The model is an additive logistic one with an explicit intercept:
/// <code>
/// logit(expected) = logit(self) + Σ (logit(ally_i) − logit(cohort))
/// </code>
/// The intercept matters. <c>self</c> is measured on tracked truemains playing
/// their signature champion, so its mean sits clearly above 50%, while an
/// <c>ally</c> rate is measured on whoever happened to share those games and sits
/// near the cohort mean. Without subtracting the cohort rate from every ally term,
/// each extra teammate would drag the expectation upward by a constant and every
/// synergy number would come out systematically negative.
/// </summary>
public static class SynergyMath
{
    /// <summary>
    /// Win rates are clamped into this open interval before taking log-odds. A
    /// sample that is above the caller's games floor can still be 100% wins
    /// (10/10), whose log-odds is infinite; clamping turns that into a large but
    /// finite advantage instead of poisoning the whole expression with infinity.
    /// </summary>
    private const double MinRate = 1e-3;

    private const double MaxRate = 1 - MinRate;

    /// <summary>
    /// Expected win rate of a group made of one tracked player (their own win rate
    /// on the champion, <paramref name="selfWinRate"/>) plus one or more allies,
    /// each described by the win rate of games in which that champion was on a
    /// tracked player's team (<paramref name="allyWinRates"/>).
    /// </summary>
    /// <param name="selfWinRate">
    /// Marginal win rate of the tracked side, in [0, 1] (clamped away from the
    /// endpoints internally).
    /// </param>
    /// <param name="allyWinRates">
    /// Marginal ally win rates, one per teammate being added to the group. Empty
    /// means "no teammate added", and the expectation is just the tracked side's
    /// own rate.
    /// </param>
    /// <param name="cohortWinRate">
    /// Win rate of the whole cohort the two other arguments were measured over —
    /// the intercept each ally term is expressed relative to.
    /// </param>
    /// <returns>The expected win rate, strictly inside (0, 1).</returns>
    public static double ExpectedWinRate(
        double selfWinRate,
        ReadOnlySpan<double> allyWinRates,
        double cohortWinRate)
    {
        var cohortLogOdds = Logit(cohortWinRate);
        var logOdds = Logit(selfWinRate);

        foreach (var allyWinRate in allyWinRates)
        {
            logOdds += Logit(allyWinRate) - cohortLogOdds;
        }

        return Sigmoid(logOdds);
    }

    /// <summary>
    /// The signed synergy of a group: how far its observed win rate lands above
    /// (positive) or below (negative) what the marginals predict. Expressed on the
    /// same 0–1 scale as a win rate, so +0.03 reads as "three points better than
    /// expected".
    /// </summary>
    /// <param name="observedWinRate">Win rate actually recorded for the group.</param>
    /// <param name="selfWinRate">Marginal win rate of the tracked side.</param>
    /// <param name="allyWinRates">Marginal ally win rates, one per teammate.</param>
    /// <param name="cohortWinRate">Cohort win rate used as the intercept.</param>
    /// <returns><paramref name="observedWinRate"/> minus the expected win rate.</returns>
    public static double Synergy(
        double observedWinRate,
        double selfWinRate,
        ReadOnlySpan<double> allyWinRates,
        double cohortWinRate)
        => observedWinRate - ExpectedWinRate(selfWinRate, allyWinRates, cohortWinRate);

    private static double Logit(double rate)
    {
        var clamped = Math.Clamp(rate, MinRate, MaxRate);
        return Math.Log(clamped / (1 - clamped));
    }

    private static double Sigmoid(double logOdds) => 1 / (1 + Math.Exp(-logOdds));
}
