using AwesomeAssertions;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The detectors (#924) exist to be believed, so the ways they can lie are what these
/// tests pin: reporting a pass it did not measure, missing the first occurrence of a
/// bug it was built for, or letting one unmeasurable row hide a failing one.
/// </summary>
public sealed class DataQualityDetectorEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Classify_FiresOnTheFirstOccurrence_NotAfterIt()
    {
        // #911 is the reason: with `>` instead of `>=`, a threshold of 1 duplicate group
        // would stay green on exactly the bug the detector was built to catch.
        DataQualityDetectorEvaluator.Classify(1L, 1, 1).Should().Be(DetectorStatus.Red);
        DataQualityDetectorEvaluator.Classify(0L, 1, 1).Should().Be(DetectorStatus.Green);
    }

    [Fact]
    public void Classify_TreatsAnUnmeasuredValueAsUnknown_NeverGreen()
    {
        DataQualityDetectorEvaluator.Classify(null, 10, 20).Should().Be(DetectorStatus.Unknown);
        DataQualityDetectorEvaluator.Classify(double.NaN, 10, 20).Should().Be(DetectorStatus.Unknown);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -5)]
    public void Classify_TreatsANonPositiveLevelAsDisabled(double amber, double red)
    {
        // This is how a warning-only detector is configured; a disabled level must be
        // unreachable rather than always-reached.
        DataQualityDetectorEvaluator.Classify(1_000_000d, amber, red).Should().Be(DetectorStatus.Green);
    }

    [Fact]
    public void Classify_ReachesAmberButNotRed_WhenOnlyRedIsDisabled()
    {
        DataQualityDetectorEvaluator.Classify(50d, 10, 0).Should().Be(DetectorStatus.Amber);
    }

    [Fact]
    public void Worst_PutsUnknownBetweenAmberAndGreen()
    {
        // Above green: a card must not claim to be clean when part of it was never
        // measured. Below red: one unmeasurable platform must not hide a failing one.
        DataQualityDetectorEvaluator
            .Worst([DetectorStatus.Green, DetectorStatus.Unknown])
            .Should().Be(DetectorStatus.Unknown);
        DataQualityDetectorEvaluator
            .Worst([DetectorStatus.Unknown, DetectorStatus.Red])
            .Should().Be(DetectorStatus.Red);
        DataQualityDetectorEvaluator
            .Worst([DetectorStatus.Unknown, DetectorStatus.Amber])
            .Should().Be(DetectorStatus.Amber);
    }

    [Fact]
    public void Worst_TreatsNothingToJudgeAsUnknown()
    {
        DataQualityDetectorEvaluator.Worst([]).Should().Be(DetectorStatus.Unknown);
    }

    [Fact]
    public void Percent_RefusesToDivideAnEmptySample()
    {
        // Returning 0 here would read as "perfectly clean" on a card that measured nothing.
        DataQualityDetectorEvaluator.Percent(0, 0).Should().BeNull();
        DataQualityDetectorEvaluator.Percent(3, 4).Should().Be(75);
    }

    [Fact]
    public void AgeHours_ReportsAFutureTimestampAsZero_NotNegative()
    {
        DataQualityDetectorEvaluator.AgeHours(Now.AddHours(3), Now).Should().Be(0);
        DataQualityDetectorEvaluator.AgeHours(Now.AddHours(-3), Now).Should().Be(3);
        DataQualityDetectorEvaluator.AgeHours(null, Now).Should().BeNull();
    }

    [Fact]
    public void ReadOrphanRatio_ReportsTheLevelFromTheNewerWindow_AndItsMovement()
    {
        var reading = DataQualityDetectorEvaluator.ReadOrphanRatio(99, 100, 90, 100);

        reading.Percent.Should().Be(99);
        reading.PreviousPercent.Should().Be(90);
        reading.RisePoints.Should().Be(9);
    }

    [Fact]
    public void ReadOrphanRatio_ReportsNoTrend_WhenTheOlderWindowIsEmpty()
    {
        // A "trend" against nothing is a full-scale jump or drop depending only on which
        // side was missing, which is worse than admitting there is no trend.
        var reading = DataQualityDetectorEvaluator.ReadOrphanRatio(50, 100, 0, 0);

        reading.Percent.Should().Be(50);
        reading.RisePoints.Should().BeNull();
    }

    [Fact]
    public void ReadOrphanRatio_StillReportsALevel_WhenOnlyTheOlderWindowHasRows()
    {
        var reading = DataQualityDetectorEvaluator.ReadOrphanRatio(0, 0, 40, 100);

        reading.Percent.Should().Be(40);
        reading.RisePoints.Should().BeNull();
    }

    [Fact]
    public void ReadPatchVolumes_NeverJudgesTheNewestOrTheOldestPatch()
    {
        // The newest is still filling and the oldest is being retention-trimmed, so both
        // are legitimately thin — flagging them would fire on a healthy pipeline daily.
        var reading = DataQualityDetectorEvaluator.ReadPatchVolumes(
            [
                new PatchVolume("16.11", 10),
                new PatchVolume("16.12", 1_000),
                new PatchVolume("16.13", 1_000),
                new PatchVolume("16.14", 1_000),
                new PatchVolume("16.15", 5)
            ],
            0.4,
            3);

        var verdicts = reading.Verdicts.ToDictionary(verdict => verdict.Patch.Patch, StringComparer.Ordinal);
        verdicts["16.11"].Judged.Should().BeFalse();
        verdicts["16.15"].Judged.Should().BeFalse();
        verdicts.Values.Should().NotContain(verdict => verdict.Thin);
        reading.MedianMatches.Should().Be(1_000);
    }

    [Fact]
    public void ReadPatchVolumes_FlagsAnInteriorPatchFarBelowTheMedian()
    {
        var reading = DataQualityDetectorEvaluator.ReadPatchVolumes(
            [
                new PatchVolume("16.11", 1_000),
                new PatchVolume("16.12", 1_000),
                new PatchVolume("16.13", 100),
                new PatchVolume("16.14", 1_000),
                new PatchVolume("16.15", 1_000)
            ],
            0.4,
            3);

        reading.Verdicts.Single(verdict => verdict.Thin).Patch.Patch.Should().Be("16.13");
    }

    [Fact]
    public void ReadPatchVolumes_DeclinesToJudge_WithoutEnoughComparablePatches()
    {
        // Two patches leave zero interior ones; a median of nothing is not a baseline.
        var reading = DataQualityDetectorEvaluator.ReadPatchVolumes(
            [new PatchVolume("16.14", 1_000), new PatchVolume("16.15", 1)],
            0.4,
            3);

        reading.MedianMatches.Should().BeNull();
        reading.ComparablePatches.Should().Be(0);
        reading.Verdicts.Should().OnlyContain(verdict => !verdict.Judged && !verdict.Thin);
    }

    [Fact]
    public void ToWireName_MapsEveryStatusToItsCamelCaseWireValue()
    {
        DetectorStatus.Green.ToWireName().Should().Be("green");
        DetectorStatus.Amber.ToWireName().Should().Be("amber");
        DetectorStatus.Red.ToWireName().Should().Be("red");
        DetectorStatus.Unknown.ToWireName().Should().Be("unknown");
    }

    [Theory]
    [InlineData(0.5, "30 min ago")]
    [InlineData(3.34, "3.3 h ago")]
    [InlineData(72, "3.0 d ago")]
    public void FormatAge_SwitchesUnitWithTheMagnitude(double hours, string expected)
    {
        DataQualityDetectorEvaluator.FormatAge(hours).Should().Be(expected);
    }

    [Fact]
    public void FormatAge_ReturnsNull_ForAnUnmeasuredAge()
    {
        DataQualityDetectorEvaluator.FormatAge(null).Should().BeNull();
    }
}
