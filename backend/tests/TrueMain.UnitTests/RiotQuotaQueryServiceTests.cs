using AwesomeAssertions;
using Data.Metrics.Mongo;
using Data.Ops.Mongo;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.UnitTests;

/// <summary>
/// The quota-utilisation and duty-cycle arithmetic of #1458, exercised on already-fetched
/// counts: per-host utilisation against the advertised binding limit, the instantaneous
/// fill from the count header, and the union (not sum) of runs per lane.
/// </summary>
public sealed class RiotQuotaQueryServiceTests
{
    private static readonly DateTime Since = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildRoute_RegionalHost_DividesCallsByWhatTheBindingLimitAllowsOverTheCoveredSpan()
    {
        // 100 calls / 120 s → 72 000 a day; 36 000 calls in a day is half of it.
        var route = Route("europe", calls: 36_000, rateLimited: 36, activeMinutes: 720,
            appLimit: "20:1,100:120", appCount: "3:1,89:120");

        var model = RiotQuotaQueryService.BuildRoute(route, TimeSpan.FromDays(1));

        model.Kind.Should().Be("regional");
        model.BindingLimit!.WindowSeconds.Should().Be(120);
        model.Utilisation.Should().BeApproximately(0.5, 1e-9);
        model.CurrentUtilisation.Should().BeApproximately(0.89, 1e-9);
        model.RateLimitedRate.Should().BeApproximately(0.001, 1e-9);
        model.CallsPerMinute.Should().BeApproximately(25, 1e-9);
        model.ActiveMinuteShare.Should().BeApproximately(0.5, 1e-9);
        model.Consumers.Single().Share.Should().Be(1);
    }

    [Fact]
    public void BuildRoute_NoRateLimitHeader_LeavesUtilisationUnmeasured()
    {
        var model = RiotQuotaQueryService.BuildRoute(
            Route("euw1", calls: 10, rateLimited: 0, activeMinutes: 1, appLimit: null, appCount: null),
            TimeSpan.FromHours(1));

        model.Kind.Should().Be("platform");
        model.Utilisation.Should().BeNull();
        model.CurrentUtilisation.Should().BeNull();
        model.BindingLimit.Should().BeNull();
    }

    [Theory]
    [InlineData("3:1,57:120", 120, 100, 0.57)]
    [InlineData("3:1", 120, 100, null)]
    [InlineData("garbage", 120, 100, null)]
    public void CurrentFill_ReadsTheBindingWindowsCount(string header, int windowSeconds, long limit, double? expected)
    {
        var fill = RiotQuotaQueryService.CurrentFill(header, windowSeconds, limit);

        if (expected is null)
        {
            fill.Should().BeNull();
        }
        else
        {
            fill.Should().BeApproximately(expected.Value, 1e-9);
        }
    }

    [Fact]
    public void BuildLanes_OverlappingRunsCountOnce_AndRunsAreClippedToTheWindow()
    {
        var now = Since.AddHours(10);
        var runs = new[]
        {
            // 1 h before the window to 1 h inside: only the inside hour counts.
            Run("FetchLane", "Discovery", Since.AddHours(-1), Since.AddHours(1)),
            // Overlaps the next run by an hour: the lane is busy 2 h-5 h, three hours.
            Run("FetchLane", "MatchIngestion", Since.AddHours(2), Since.AddHours(4)),
            Run("FetchLane", "Harvest", Since.AddHours(3), Since.AddHours(5)),
            Run("AggregateLane", "Scoring", Since.AddHours(6), Since.AddHours(7)),
            Run(null, "ManualSeed", Since.AddHours(8), Since.AddHours(8))
        };

        var lanes = RiotQuotaQueryService.BuildLanes(runs, Since, now);

        lanes.Select(l => l.Lane).Should().Equal("FetchLane", "AggregateLane", "unassigned");
        var fetch = lanes[0];
        fetch.BusyHours.Should().BeApproximately(4, 1e-9);
        fetch.DutyCycle.Should().BeApproximately(0.4, 1e-9);
        fetch.Runs.Should().Be(3);
        fetch.Processes.Select(p => p.ProcessName).Should().Equal("Harvest", "MatchIngestion", "Discovery");
        fetch.Processes.Sum(p => p.BusyHours).Should().BeApproximately(5, 1e-9);

        var unassigned = lanes[2];
        unassigned.Runs.Should().Be(1);
        unassigned.DutyCycle.Should().Be(0);
    }

    [Fact]
    public void UnionLength_MergesNestedAndTouchingIntervals()
    {
        var union = RiotQuotaQueryService.UnionLength(
        [
            (Since, Since.AddHours(4)),
            (Since.AddHours(1), Since.AddHours(2)),
            (Since.AddHours(4), Since.AddHours(5)),
            (Since.AddHours(7), Since.AddHours(8))
        ]);

        union.Should().Be(TimeSpan.FromHours(6));
    }

    private static RiotRouteUsage Route(
        string route,
        long calls,
        long rateLimited,
        long activeMinutes,
        string? appLimit,
        string? appCount)
        => new(route, calls, rateLimited, rateLimited, activeMinutes, appLimit, appCount,
            appCount is null ? null : Since,
            [new RiotRouteConsumer("MatchIngestion", "match-v5.match", calls, rateLimited)]);

    private static ProcessRunInterval Run(string? lane, string process, DateTime start, DateTime end)
        => new(lane, process, start, end);
}
