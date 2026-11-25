using AwesomeAssertions;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.UnitTests;

/// <summary>
/// The time buckets the ops run charts (matches ingested, candidate funnel) share, tested once
/// here rather than through each chart.
/// </summary>
public sealed class RunTimeBucketsTests
{
    // A Wednesday afternoon.
    private static readonly DateTime Instant = new(2026, 8, 5, 14, 37, 12, DateTimeKind.Utc);

    [Theory]
    [InlineData(IngestionTimeGranularity.Hour, "2026-08-05T14:00:00Z")]
    [InlineData(IngestionTimeGranularity.Day, "2026-08-05T00:00:00Z")]
    // Weeks start on Monday, as Postgres' date_trunc('week') does for the sibling charts.
    [InlineData(IngestionTimeGranularity.Week, "2026-08-03T00:00:00Z")]
    [InlineData(IngestionTimeGranularity.Month, "2026-08-01T00:00:00Z")]
    public void Truncate_PutsAnInstantInTheBucketThatStartsIt(IngestionTimeGranularity granularity, string expected)
        => RunTimeBuckets.Format(RunTimeBuckets.Truncate(Instant, granularity)).Should().Be(expected);

    [Fact]
    public void Truncate_KeepsASundayInTheWeekThatStartedTheMondayBefore()
        => RunTimeBuckets.Format(RunTimeBuckets.Truncate(new DateTime(2026, 8, 9, 23, 0, 0, DateTimeKind.Utc), IngestionTimeGranularity.Week))
            .Should().Be("2026-08-03T00:00:00Z");

    [Theory]
    [InlineData(IngestionTimeGranularity.Hour, "2026-12-31T23:00:00Z", "2027-01-01T00:00:00Z")]
    [InlineData(IngestionTimeGranularity.Day, "2026-12-31T00:00:00Z", "2027-01-01T00:00:00Z")]
    [InlineData(IngestionTimeGranularity.Week, "2026-12-28T00:00:00Z", "2027-01-04T00:00:00Z")]
    [InlineData(IngestionTimeGranularity.Month, "2026-12-01T00:00:00Z", "2027-01-01T00:00:00Z")]
    public void Advance_StepsToTheNextBucketAcrossAYearEnd(IngestionTimeGranularity granularity, string bucket, string next)
        => RunTimeBuckets.Format(RunTimeBuckets.Advance(
                DateTime.Parse(bucket, null, System.Globalization.DateTimeStyles.AdjustToUniversal), granularity))
            .Should().Be(next);
}
