using AwesomeAssertions;
using Core.Lol.Benchmarks;

namespace TrueMain.UnitTests;

public sealed class FormHistogramTests
{
    [Theory]
    [InlineData(FormMetric.Kda, 0.3, 3)]
    [InlineData(FormMetric.Kda, 2.67, 26)]
    [InlineData(FormMetric.KillParticipation, 0.58, 58)]
    [InlineData(FormMetric.CsPerMinute, 7.04, 70)]
    [InlineData(FormMetric.Deaths, 4, 4)]
    [InlineData(FormMetric.VisionPerMinute, 1.12, 22)]
    public void ToBucket_PutsAValueInTheBinItNames(FormMetric metric, double value, int bucket)
        => FormHistogram.ToBucket(metric, value).Should().Be(bucket);

    [Fact]
    public void ToBucket_ClampsToZeroAndTheCeiling()
    {
        FormHistogram.ToBucket(FormMetric.Kda, -1).Should().Be(0);
        FormHistogram.ToBucket(FormMetric.Kda, double.NaN).Should().Be(0);
        FormHistogram.ToBucket(FormMetric.Kda, 1000).Should().Be(300);
        FormHistogram.ToBucket(FormMetric.KillParticipation, 1.4).Should().Be(100);
    }

    [Fact]
    public void Percentile_InterpolatesInsideTheBinItFallsIn()
    {
        var histogram = new List<(int, long)> { (25, 10), (26, 10) };

        FormHistogram.Percentile(FormMetric.Kda, histogram, 0.5)!.Value.Should().BeApproximately(2.6, 1e-9);
        FormHistogram.Percentile(FormMetric.Kda, histogram, 0.25)!.Value.Should().BeApproximately(2.55, 1e-9);
    }
}
