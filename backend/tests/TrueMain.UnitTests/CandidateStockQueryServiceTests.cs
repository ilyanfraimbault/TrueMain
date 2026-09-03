using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using NSubstitute;
using TrueMain.Services.Ops;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// The stock series' two hard parts (#1403): a level is <em>sampled</em> across time but
/// <em>summed</em> across platforms — get the two reductions the wrong way round and a
/// day of hourly readings reports twenty-four times the queue that exists — and a period
/// with no snapshot must be absent rather than zero.
/// </summary>
public sealed class CandidateStockQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_SumsPlatformsWithinOneReading()
    {
        var hour = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc);
        var store = StoreWith(
            Point(hour, "EUW1", "Queued", 300),
            Point(hour, "KR", "Queued", 120),
            Point(hour, "EUW1", "Validated", 50),
            Point(hour, "KR", "Validated", 25));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Hour, windowDays: 7, CancellationToken.None);

        var bucket = result.Buckets.Should().ContainSingle().Subject;
        bucket.Queued.Should().Be(420, "platforms are disjoint populations at one instant");
        bucket.Validated.Should().Be(75);
    }

    [Fact]
    public async Task GetAsync_KeepsTheLastReadingOfAPeriod_RatherThanAddingThemUp()
    {
        var day = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
        var store = StoreWith(
            Point(day.AddHours(1), "EUW1", "Queued", 400),
            Point(day.AddHours(2), "EUW1", "Queued", 410),
            Point(day.AddHours(23), "EUW1", "Queued", 380));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 7, CancellationToken.None);

        var bucket = result.Buckets.Should().ContainSingle().Subject;
        bucket.Queued.Should().Be(380, "a stock is a level, and three readings of it are not three queues");
        bucket.SampledAtUtc.Should().Be(day.AddHours(23));
    }

    [Fact]
    public async Task GetAsync_ReportsARecordedZero_ButLeavesAnUnmeasuredPeriodOutEntirely()
    {
        var measured = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc);
        var store = StoreWith(
            Point(measured, "EUW1", "New", 0),
            Point(measured, "EUW1", "Queued", 300));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Hour, windowDays: 7, CancellationToken.None);

        result.Buckets.Should().ContainSingle().Which.New.Should()
            .Be(0, "scoring having drained its backlog is a measurement, not a gap");
        result.Buckets.Should().HaveCount(1, "the 167 other hours of the window were never measured");
        result.EarliestSnapshotAtUtc.Should().Be(measured);
        result.LatestSnapshotAtUtc.Should().Be(measured);
    }

    [Fact]
    public async Task GetAsync_ReturnsAnEmptyModel_WhenNothingWasEverSnapshotted()
    {
        var result = await CreateService(StoreWith()).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 30, CancellationToken.None);

        result.Buckets.Should().BeEmpty("an unmeasured window is not a window of zeros");
        result.EarliestSnapshotAtUtc.Should().BeNull();
        result.LatestSnapshotAtUtc.Should().BeNull();
        result.RetentionDays.Should().Be(90);
    }

    [Fact]
    public async Task GetAsync_IgnoresAStatusThisBuildDoesNotKnow()
    {
        var hour = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc);
        var store = StoreWith(
            Point(hour, "EUW1", "Queued", 300),
            Point(hour, "EUW1", "Quarantined", 999));

        var result = await CreateService(store).GetAsync(
            IngestionTimeGranularity.Hour, windowDays: 7, CancellationToken.None);

        var bucket = result.Buckets.Should().ContainSingle().Subject;
        bucket.Queued.Should().Be(300);
        (bucket.New + bucket.Scored + bucket.Processing + bucket.Validated + bucket.Rejected).Should()
            .Be(0, "an unknown status must not be folded into a series it does not belong to");
    }

    [Fact]
    public async Task GetAsync_ClampsTheWindow()
    {
        var result = await CreateService(StoreWith()).GetAsync(
            IngestionTimeGranularity.Day, windowDays: 100_000, CancellationToken.None);

        result.WindowDays.Should().Be(365);
    }

    private static CandidateStockSnapshotPoint Point(DateTime hour, string platform, string status, long count)
        => new(hour, platform, status, count);

    /// <summary>
    /// The store stub, serving points oldest-first the way the real one does — the
    /// service relies on that order to date the series' first and last readings.
    /// </summary>
    private static ICandidateStockSnapshotStore StoreWith(params CandidateStockSnapshotPoint[] points)
    {
        var store = Substitute.For<ICandidateStockSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CandidateStockSnapshotPoint>>(
                _ => [.. points.OrderBy(point => point.SnapshotHourUtc)]);
        return store;
    }

    private static CandidateStockQueryService CreateService(ICandidateStockSnapshotStore store)
        => new(
            store,
            Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
            {
                CandidateStockSnapshotsRetention = TimeSpan.FromDays(90)
            }),
            new FixedTimeProvider(Now));
}
