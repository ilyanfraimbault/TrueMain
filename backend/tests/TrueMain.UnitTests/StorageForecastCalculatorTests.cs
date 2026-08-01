using AwesomeAssertions;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The forecast (#925) is the one part of the storage dashboard that invents a number
/// rather than reporting one, so its refusals matter as much as its arithmetic: it
/// must decline to project rather than guess when the data cannot support a line.
/// </summary>
public sealed class StorageForecastCalculatorTests
{
    private static readonly DateTime Day0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Project_FitsAConstantDailyGrowthExactly()
    {
        // 1 GiB/day, dead straight: the fit must recover the slope and land the
        // crossing on the exact day the line reaches the threshold.
        const long perDay = 1024L * 1024 * 1024;
        var points = Enumerable.Range(0, 10)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), 10 * perDay + (day * perDay)))
            .ToList();

        var forecast = StorageForecastCalculator.Project(points, [20 * perDay]);

        forecast.Should().NotBeNull();
        forecast!.BytesPerDay.Should().Be(perDay);
        forecast.Crossings.Should().ContainSingle();
        // Size is 10 GiB at day 0 and grows 1 GiB/day, so 20 GiB is day 10.
        forecast.Crossings[0].ProjectedAtUtc.Should().BeCloseTo(Day0.AddDays(10), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Project_ReturnsNull_WhenTooFewDaysToFit()
    {
        var points = new List<StorageForecastPoint>
        {
            new(Day0, 100),
            new(Day0.AddDays(1), 200),
        };

        // Two points fit a line perfectly, which would report a crossing date with
        // total confidence off what might be one retention pass.
        StorageForecastCalculator.Project(points, [1000]).Should().BeNull();
    }

    [Fact]
    public void Project_ReturnsNull_WhenStorageIsFlat()
    {
        var points = Enumerable.Range(0, 7)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), 5_000_000))
            .ToList();

        // A flat series never crosses anything; extrapolating it would be division
        // by a zero slope.
        StorageForecastCalculator.Project(points, [9_000_000]).Should().BeNull();
    }

    [Fact]
    public void Project_ReturnsNull_WhenStorageIsShrinking()
    {
        var points = Enumerable.Range(0, 7)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), 5_000_000 - (day * 100_000)))
            .ToList();

        // Retention winning is not a disk-exhaustion forecast.
        StorageForecastCalculator.Project(points, [9_000_000]).Should().BeNull();
    }

    [Fact]
    public void Project_ReturnsNull_WhenEveryPointFallsOnTheSameInstant()
    {
        var points = Enumerable.Repeat(new StorageForecastPoint(Day0, 5_000_000), 5).ToList();

        // Zero variance in x — the regression would divide by zero.
        StorageForecastCalculator.Project(points, [9_000_000]).Should().BeNull();
    }

    [Fact]
    public void Project_ReportsAPastDate_WhenTheThresholdIsAlreadyPassed()
    {
        var points = Enumerable.Range(0, 5)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), 10_000 + (day * 1_000)))
            .ToList();

        var forecast = StorageForecastCalculator.Project(points, [5_000]);

        // Already over the line: the honest answer is a date in the past, not null.
        forecast.Should().NotBeNull();
        forecast!.Crossings[0].ProjectedAtUtc.Should().NotBeNull();
        forecast.Crossings[0].ProjectedAtUtc!.Value.Should().BeBefore(Day0);
    }

    [Fact]
    public void Project_ReportsNoCrossing_WhenItIsFurtherOutThanACentury()
    {
        // One byte a day against a terabyte: mathematically a crossing exists, but
        // reporting the year 2,700,000 would be noise dressed as information.
        var points = Enumerable.Range(0, 30)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), 1_000 + day))
            .ToList();

        var forecast = StorageForecastCalculator.Project(points, [1_000_000_000_000]);

        forecast.Should().NotBeNull();
        forecast!.Crossings[0].ProjectedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Project_ReportsNoCrossing_WhenTheThresholdWasPassedFurtherBackThanACentury()
    {
        // The mirror of the century-out case, and the one that used to throw: a
        // threshold far below the fitted intercept combined with a barely-positive
        // slope puts the crossing hundreds of thousands of years in the past, which
        // underflows DateTime and took the whole /ops/db/history endpoint down with
        // it — not just the forecast card. Exactly the "already near capacity, growth
        // has plateaued" shape this panel exists to surface (#680).
        var points = Enumerable.Range(0, 30)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), 500_000_000_000 + day))
            .ToList();

        var act = () => StorageForecastCalculator.Project(points, [1_000]);

        act.Should().NotThrow();
        act()!.Crossings[0].ProjectedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Project_IsRobustToNoiseAroundTheTrend()
    {
        // Real snapshots jitter (vacuum, TOAST churn). The fit must track the trend
        // rather than the last two points.
        const long perDay = 1_000_000;
        var jitter = new[] { 0, 250_000, -300_000, 120_000, -90_000, 200_000, -150_000, 40_000 };
        var points = jitter
            .Select((noise, day) => new StorageForecastPoint(
                Day0.AddDays(day),
                (10 * perDay) + (day * perDay) + noise))
            .ToList();

        var forecast = StorageForecastCalculator.Project(points, [20 * perDay]);

        forecast.Should().NotBeNull();
        forecast!.BytesPerDay.Should().BeCloseTo(perDay, 100_000);
    }

    [Fact]
    public void Project_ReturnsOneCrossingPerThreshold_InOrder()
    {
        const long perDay = 1_000_000;
        var points = Enumerable.Range(0, 6)
            .Select(day => new StorageForecastPoint(Day0.AddDays(day), day * perDay))
            .ToList();

        var forecast = StorageForecastCalculator.Project(points, [10 * perDay, 20 * perDay, 30 * perDay]);

        forecast.Should().NotBeNull();
        forecast!.Crossings.Select(crossing => crossing.ThresholdBytes)
            .Should().Equal(10 * perDay, 20 * perDay, 30 * perDay);
        forecast.Crossings.Select(crossing => crossing.ProjectedAtUtc)
            .Should().BeInAscendingOrder();
    }
}
