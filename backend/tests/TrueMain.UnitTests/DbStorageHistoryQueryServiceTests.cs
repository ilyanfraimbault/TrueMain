using AwesomeAssertions;
using Data.Metrics.Mongo;
using NSubstitute;
using TrueMain.Options;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The window parameter reaches <see cref="DateTime.AddDays"/> from the query string,
/// so it is the endpoint's one unvalidated arithmetic input (#925).
/// </summary>
public sealed class DbStorageHistoryQueryServiceTests
{
    [Theory]
    [InlineData(999_999_999)]
    [InlineData(int.MaxValue)]
    public async Task GetAsync_ClampsAnAbsurdWindow_InsteadOfOverflowingTheDate(int windowDays)
    {
        // Unclamped this threw ArgumentOutOfRangeException out of AddDays and 500'd the
        // whole endpoint — charts and table growth included, not just the forecast.
        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var service = CreateService(store);

        var act = async () => await service.GetAsync(windowDays, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await store.Received(1).GetHistoryAsync(
            Arg.Is<DateTime>(since => since > DateTime.UtcNow.AddDays(-731)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetAsync_FallsBackToTheConfiguredWindow_WhenNoneIsUsable(int? windowDays)
    {
        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var service = CreateService(store, defaultWindowDays: 30);

        await service.GetAsync(windowDays, CancellationToken.None);

        // 30 days back, give or take the date truncation.
        await store.Received(1).GetHistoryAsync(
            Arg.Is<DateTime>(since =>
                since <= DateTime.UtcNow.AddDays(-30) && since > DateTime.UtcNow.AddDays(-32)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ReturnsAnEmptyModel_WhenNoSnapshotsExistYet()
    {
        // Mongo unconfigured, or the ingestor step has never run. The panel must show
        // "no history yet", not fail.
        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateService(store).GetAsync(null, CancellationToken.None);

        result.Daily.Should().BeEmpty();
        result.Tables.Should().BeEmpty();
        result.Forecast.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_LeavesTheForecastNull_WhenNoDiskCapacityIsConfigured()
    {
        // Plenty of history and clear growth, but nothing to be a percentage of.
        var day0 = DateTime.UtcNow.Date.AddDays(-5);
        var points = Enumerable.Range(0, 5)
            .Select(day => new DbTableSizeSnapshotPoint(
                day0.AddDays(day), StorageEngines.Postgres, "matches",
                1_000 * (day + 1), 2_000 * (day + 1), 1_500, 500,
                10_000_000 + (day * 1_000_000)))
            .ToList();

        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(points);

        var result = await CreateService(store).GetAsync(null, CancellationToken.None);

        result.Daily.Should().HaveCount(5);
        result.Forecast.Should().BeNull("a forecast against a guessed capacity is worse than none");
    }

    [Fact]
    public async Task GetAsync_SumsTheEnginesIntoTheDayInsteadOfTakingTheLarger()
    {
        // Both engines share one volume, so the disk figure is the sum. Taking the max
        // — what the read did before #1023 — would have reported 10 GB here and hidden
        // Mongo's 3 GB entirely.
        var day0 = DateTime.UtcNow.Date.AddDays(-2);
        var points = new List<DbTableSizeSnapshotPoint>();
        foreach (var day in Enumerable.Range(0, 3))
        {
            points.Add(new DbTableSizeSnapshotPoint(
                day0.AddDays(day), StorageEngines.Postgres, "matches", 1_000, 2_000, 1_500, 500,
                10_000_000_000));
            points.Add(new DbTableSizeSnapshotPoint(
                day0.AddDays(day), StorageEngines.Mongo, "logs", 900, 800, 600, 200,
                3_000_000_000));
        }

        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(points);

        var result = await CreateService(store).GetAsync(null, CancellationToken.None);

        result.ComparableDays.Should().Be(3, "every day measures the same two engines");
        result.Daily.Should().OnlyContain(point => point.DatabaseBytes == 13_000_000_000L);
        result.Daily.Should().OnlyContain(point => point.PostgresBytes == 10_000_000_000L);
        result.Daily.Should().OnlyContain(point => point.MongoBytes == 3_000_000_000L);
        result.Engines.Should().Equal([StorageEngines.Mongo, StorageEngines.Postgres]);
    }

    [Fact]
    public async Task GetAsync_KeepsSameNamedObjectsOfDifferentEnginesApart()
    {
        // process_runs is both a (frozen) Postgres table and a Mongo collection.
        // Collapsing them on name alone would add one's size to the other's.
        var day = DateTime.UtcNow.Date.AddDays(-1);
        var points = new List<DbTableSizeSnapshotPoint>
        {
            new(day, StorageEngines.Postgres, "process_runs", 10, 4_096, 3_000, 1_096, 5_000),
            new(day, StorageEngines.Mongo, "process_runs", 20, 8_192, 6_000, 2_192, 9_000),
        };

        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(points);

        var result = await CreateService(store).GetAsync(null, CancellationToken.None);

        result.Tables.Should().HaveCount(2);
        result.Tables.Should().ContainSingle(series =>
            series.Engine == StorageEngines.Postgres && series.CurrentBytes == 4_096);
        result.Tables.Should().ContainSingle(series =>
            series.Engine == StorageEngines.Mongo && series.CurrentBytes == 8_192);
    }

    [Fact]
    public async Task GetAsync_DoesNotFitTheForecastAcrossTheDayANewEngineStartedBeingMeasured()
    {
        // Four flat Postgres-only days, then Mongo appears and adds its footprint in
        // one step. That step is not growth: fitted whole, the series would read as a
        // steep daily climb and forecast a saturation that is not coming. Only the
        // comparable tail is fitted, and one day is not enough to fit anything.
        var day0 = DateTime.UtcNow.Date.AddDays(-4);
        var points = new List<DbTableSizeSnapshotPoint>();
        foreach (var day in Enumerable.Range(0, 4))
        {
            points.Add(new DbTableSizeSnapshotPoint(
                day0.AddDays(day), StorageEngines.Postgres, "matches", 1_000, 2_000, 1_500, 500,
                10_000_000_000));
        }

        var lastDay = day0.AddDays(4);
        points.Add(new DbTableSizeSnapshotPoint(
            lastDay, StorageEngines.Postgres, "matches", 1_000, 2_000, 1_500, 500, 10_000_000_000));
        points.Add(new DbTableSizeSnapshotPoint(
            lastDay, StorageEngines.Mongo, "logs", 900, 800, 600, 200, 3_000_000_000));

        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(points);

        var result = await CreateService(store, diskCapacityBytes: 200_000_000_000)
            .GetAsync(null, CancellationToken.None);

        result.Daily.Should().HaveCount(5, "every measured day is still charted");
        result.Forecast.Should().BeNull(
            "the only comparable day is the one Mongo first appeared on");
        // The panel names the reason off this number rather than re-deriving the rule,
        // so it has to say "1 of the 5 charted days is comparable", not just "no
        // forecast".
        result.ComparableDays.Should().Be(1);
    }

    private static DbStorageHistoryQueryService CreateService(
        IDbStorageSnapshotStore store,
        int defaultWindowDays = 90,
        long diskCapacityBytes = 0)
        => new(
            store,
            // Fully qualified: the `TrueMain.Options` using above shadows the
            // `Options` static class, the same way the integration tests spell it out.
            Microsoft.Extensions.Options.Options.Create(new StorageHistoryOptions
            {
                DefaultWindowDays = defaultWindowDays,
                DiskCapacityBytes = diskCapacityBytes,
            }),
            TimeProvider.System);
}
