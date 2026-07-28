using AwesomeAssertions;
using Core.Lol.Synergy;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the properties the champion-synergies read (#922) relies on: that the
/// expected win rate stays a probability, that an ally at the cohort mean is
/// neutral, and that the metric answers "better than the sum of its parts"
/// rather than "wins a lot".
/// </summary>
public sealed class SynergyMathTests
{
    private const double Cohort = 0.5;

    [Fact]
    public void NoAlly_ExpectsTheTrackedSidesOwnRate()
    {
        SynergyMath.ExpectedWinRate(0.57, [], Cohort).Should().BeApproximately(0.57, 1e-9);
    }

    [Fact]
    public void AllyAtTheCohortMean_LeavesTheExpectationUnchanged()
    {
        // A partner who wins exactly as often as the cohort adds no information,
        // so pairing with them must not move the bar the observed rate is judged
        // against. This is the property the cohort intercept exists for.
        SynergyMath.ExpectedWinRate(0.57, [Cohort], Cohort).Should().BeApproximately(0.57, 1e-9);
        SynergyMath.ExpectedWinRate(0.57, [Cohort, Cohort], Cohort).Should().BeApproximately(0.57, 1e-9);
    }

    [Fact]
    public void AllyAboveTheCohortMean_RaisesTheExpectation_AndBelowLowersIt()
    {
        var strong = SynergyMath.ExpectedWinRate(0.52, [0.56], Cohort);
        var weak = SynergyMath.ExpectedWinRate(0.52, [0.44], Cohort);

        strong.Should().BeGreaterThan(0.52);
        weak.Should().BeLessThan(0.52);
    }

    [Fact]
    public void ExpectationsStayProbabilities_EvenForExtremeInputs()
    {
        // Percentage addition would have claimed 130% here. Log-odds addition
        // saturates instead, and the 0/1 clamp keeps a 100%-wins sample finite.
        var extreme = SynergyMath.ExpectedWinRate(0.9, [0.9, 0.9], Cohort);
        extreme.Should().BeGreaterThan(0.9).And.BeLessThan(1.0);

        var perfect = SynergyMath.ExpectedWinRate(1.0, [1.0], Cohort);
        perfect.Should().BeGreaterThan(0.0).And.BeLessThan(1.0);

        var hopeless = SynergyMath.ExpectedWinRate(0.0, [0.0], Cohort);
        hopeless.Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
    }

    [Fact]
    public void AllyOrderDoesNotMatter()
    {
        SynergyMath.ExpectedWinRate(0.53, [0.58, 0.47], Cohort)
            .Should().BeApproximately(SynergyMath.ExpectedWinRate(0.53, [0.47, 0.58], Cohort), 1e-12);
    }

    [Fact]
    public void ACohortAboveFiftyPercent_IsTheBarAlliesAreMeasuredAgainst()
    {
        // The tracked population wins more than half its games (truemains on their
        // signature champion), so an ally sitting at that elevated mean must still
        // be neutral. Without the intercept the expectation would drift upward and
        // every synergy would read negative.
        const double trackedCohort = 0.54;
        SynergyMath.ExpectedWinRate(0.60, [trackedCohort], trackedCohort)
            .Should().BeApproximately(0.60, 1e-9);
    }

    [Fact]
    public void Synergy_IsObservedMinusExpected_NotRawWinRate()
    {
        // Two champions that each win a lot, together at 60%: strong on paper, but
        // below what their marginals already promised, so the synergy is negative.
        var strongPairUnderdelivering = SynergyMath.Synergy(0.60, 0.58, [0.56], Cohort);

        // Two ordinary champions at 54%: a weaker raw number, genuinely better than
        // the parts.
        var ordinaryPairOverdelivering = SynergyMath.Synergy(0.54, 0.50, [0.50], Cohort);

        strongPairUnderdelivering.Should().BeNegative();
        ordinaryPairOverdelivering.Should().BeApproximately(0.04, 1e-9);
        ordinaryPairOverdelivering.Should().BeGreaterThan(strongPairUnderdelivering);
    }

    [Fact]
    public void Synergy_IsZero_WhenTheObservedRateMatchesTheModel()
    {
        var expected = SynergyMath.ExpectedWinRate(0.55, [0.52, 0.49], Cohort);

        SynergyMath.Synergy(expected, 0.55, [0.52, 0.49], Cohort).Should().BeApproximately(0, 1e-12);
    }
}
