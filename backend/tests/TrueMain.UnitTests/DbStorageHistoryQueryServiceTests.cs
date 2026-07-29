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
                day0.AddDays(day), "matches", 1_000 * (day + 1), 2_000 * (day + 1), 1_500, 500,
                10_000_000 + (day * 1_000_000)))
            .ToList();

        var store = Substitute.For<IDbStorageSnapshotStore>();
        store.GetHistoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(points);

        var result = await CreateService(store).GetAsync(null, CancellationToken.None);

        result.Daily.Should().HaveCount(5);
        result.Forecast.Should().BeNull("a forecast against a guessed capacity is worse than none");
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
