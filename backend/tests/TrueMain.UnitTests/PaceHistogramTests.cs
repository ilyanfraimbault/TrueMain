using AwesomeAssertions;
using Core.Lol.Pace;

namespace TrueMain.UnitTests;

public sealed class PaceHistogramTests
{
    [Theory]
    [InlineData(PaceMetric.Cs, 0, 0)]
    [InlineData(PaceMetric.Cs, 4, 0)]
    [InlineData(PaceMetric.Cs, 5, 1)]
    [InlineData(PaceMetric.Cs, 87, 17)]
    [InlineData(PaceMetric.GoldEarned, 500, 2)]
    [InlineData(PaceMetric.GoldEarned, -3, 0)]
    public void ToBucket_DividesByTheMetricsWidth(PaceMetric metric, int value, int expected)
        => PaceHistogram.ToBucket(metric, value).Should().Be(expected);

    [Fact]
    public void Percentile_InterpolatesInsideTheBinItFallsIn()
    {
        // 10 samples in [50, 55) and 10 in [55, 60): the median is the boundary.
        (int, long)[] histogram = [(10, 10), (11, 10)];

        PaceHistogram.Percentile(PaceMetric.Cs, histogram, 0.5).Should().Be(55);
        PaceHistogram.Percentile(PaceMetric.Cs, histogram, 0.25).Should().Be(52.5);
        PaceHistogram.Percentile(PaceMetric.Cs, histogram, 0.75).Should().Be(57.5);
    }

    [Fact]
    public void Percentile_IgnoresTheOrderTheBinsArriveIn()
    {
        (int, long)[] histogram = [(30, 1), (10, 3)];

        PaceHistogram.Percentile(PaceMetric.GoldEarned, histogram, 0.5).Should().BeApproximately(2000 + (2 / 3d) * 200, 1e-9);
    }

    [Fact]
    public void Percentile_IsNullForAnEmptyHistogram()
        => PaceHistogram.Percentile(PaceMetric.Cs, [], 0.5).Should().BeNull();
}
