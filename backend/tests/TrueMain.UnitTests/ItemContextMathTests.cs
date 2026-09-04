using AwesomeAssertions;
using Core.Lol.ItemContext;

namespace TrueMain.UnitTests;

/// <summary>
/// The two-proportion test behind a situational verdict (#1450).
/// </summary>
public sealed class ItemContextMathTests
{
    [Fact]
    public void ZIsPositiveWhenTheFirstRateIsHigher_AndNegativeWhenItIsLower()
    {
        ItemContextMath.TwoProportionZ(620, 1000, 210, 1000).Should().BePositive();
        ItemContextMath.TwoProportionZ(210, 1000, 620, 1000).Should().BeNegative();
    }

    [Fact]
    public void ALargeGapOnALargeSampleIsSignificant()
        => ItemContextMath.IsSignificant(620, 1000, 210, 1000, 1.96).Should().BeTrue();

    [Fact]
    public void TheSameGapOnAHandfulOfGamesIsNot()
        => ItemContextMath.IsSignificant(6, 10, 2, 10, 1.96).Should().BeFalse();

    [Fact]
    public void ATinyGapOnAHugeSampleIsSignificant_WhichIsWhyTheCallerAlsoHoldsAnAbsoluteFloor()
    {
        // 50.5% against 49.5% over 100k games each: real, and worth nothing to a reader.
        ItemContextMath.IsSignificant(50_500, 100_000, 49_500, 100_000, 1.96).Should().BeTrue();
    }

    [Fact]
    public void AnEmptySampleIsNoEvidence()
    {
        ItemContextMath.TwoProportionZ(0, 0, 5, 10).Should().Be(0d);
        ItemContextMath.TwoProportionZ(5, 10, 0, 0).Should().Be(0d);
    }

    [Fact]
    public void UnanimityIsNoEvidenceEither()
    {
        // Every game in both buckets built it: the pooled rate is 1, the standard error is
        // 0, and there is nothing left for the test to measure.
        ItemContextMath.TwoProportionZ(10, 10, 20, 20).Should().Be(0d);
        ItemContextMath.TwoProportionZ(0, 10, 0, 20).Should().Be(0d);
    }
}
