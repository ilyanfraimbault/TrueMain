using AwesomeAssertions;
using Data.Metrics.Mongo;
using TrueMain.Services.Ops.Accounts;
using TrueMain.Services.Ops.Candidates;
using TrueMain.Services.Ops.Configuration;
using TrueMain.Services.Ops.Coverage;
using TrueMain.Services.Ops.Database;
using TrueMain.Services.Ops.DataQuality;
using TrueMain.Services.Ops.Diagnostics;
using TrueMain.Services.Ops.Health;
using TrueMain.Services.Ops.Processes;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.UnitTests;

/// <summary>
/// The budget-headroom arithmetic (#1035), per routing host since #1894: each host against
/// its own advertised limit, the binding host, the "not enough to trust an extrapolation"
/// guards (route-keyed history included), and the <c>X-App-Rate-Limit</c> pair parser.
/// Exercises the internal statics directly (<c>InternalsVisibleTo</c>) — no Mongo/Postgres
/// involved, since this is pure arithmetic over already-fetched inputs.
/// </summary>
public sealed class RiotApiUsageQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Since = Now.AddDays(-7);

    // 100 calls / 120 s -> 72,000 calls/day sustained; the 1 s window allows far more.
    private const string AppLimit = "20:1,100:120";

    [Fact]
    public void BuildHeadroom_RouteKeyedHistoryBelowTwentyFourHours_IsInsufficientData()
    {
        // Seven days of rollups may exist, but only 2 h of them are route-keyed: the
        // per-host rate cannot be trusted yet.
        var inputs = Inputs(Now.AddHours(-2), Route("europe", 10_000, AppLimit));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.SufficientData.Should().BeFalse();
        headroom.ObservedWindowHours.Should().BeApproximately(2, 0.001);
        headroom.RequiredWindowHours.Should().Be(24);
        headroom.TrackedAccounts.Should().Be(100);
        headroom.AdditionalAccountsHeadroom.Should().BeNull();
        headroom.BindingRoute.Should().BeNull();
        headroom.Routes.Should().BeEmpty();
    }

    [Fact]
    public void BuildHeadroom_NoRouteKeyedRollupAtAll_IsInsufficientDataWithNothingObserved()
    {
        var headroom = RiotApiUsageQueryService.BuildHeadroom(Inputs(oldestRouteKeyed: null), 100, Now);

        headroom.SufficientData.Should().BeFalse();
        headroom.ObservedWindowHours.Should().Be(0);
    }

    [Fact]
    public void BuildHeadroom_NoTrackedAccounts_IsInsufficientData()
    {
        var inputs = Inputs(Since.AddDays(-3), Route("europe", 10_000, AppLimit));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 0, Now);

        headroom.SufficientData.Should().BeFalse();
        headroom.TrackedAccounts.Should().Be(0);
    }

    [Fact]
    public void BuildHeadroom_NoHostAdvertisedALimit_IsInsufficientData()
    {
        var inputs = Inputs(Since, Route("europe", 10_000, null), Route("euw1", 5_000, null));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.SufficientData.Should().BeFalse();
        headroom.ObservedWindowHours.Should().BeApproximately(168, 0.001);
    }

    [Fact]
    public void BuildHeadroom_EachHostAgainstItsOwnLimit_TheTightestOneBinds()
    {
        // 7 days, 100 tracked accounts, every host advertising 72,000 calls/day.
        //   europe: 350,000 calls -> 50,000/day, 500/account/day, 22,000 spare -> 44 accounts.
        //   euw1:   70,000 calls  -> 10,000/day, 100/account/day, 62,000 spare -> 620 accounts.
        // Summed, the two would read 60,000/day against one 72,000 ceiling (120 more
        // accounts): the pre-#1894 figure, which describes no real budget.
        var inputs = Inputs(
            Since.AddDays(-10),
            Route("euw1", 70_000, AppLimit),
            Route("europe", 350_000, AppLimit));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.SufficientData.Should().BeTrue();
        headroom.ObservedWindowHours.Should().BeApproximately(168, 0.001);
        headroom.BindingRoute.Should().Be("europe");
        headroom.AdditionalAccountsHeadroom.Should().Be(44);
        headroom.Routes.Select(r => r.Route).Should().Equal("europe", "euw1");

        var europe = headroom.Routes[0];
        europe.Kind.Should().Be("regional");
        europe.ObservedCallsPerDay.Should().BeApproximately(50_000, 0.001);
        europe.CallsPerAccountPerDay.Should().BeApproximately(500, 0.001);
        europe.BindingLimit!.Limit.Should().Be(100);
        europe.BindingLimit.WindowSeconds.Should().Be(120);
        europe.SpareCallsPerDay!.Value.Should().BeApproximately(22_000, 0.001);

        var euw1 = headroom.Routes[1];
        euw1.Kind.Should().Be("platform");
        euw1.SpareCallsPerDay!.Value.Should().BeApproximately(62_000, 0.001);
        euw1.AdditionalAccountsHeadroom.Should().Be(620);
    }

    [Fact]
    public void BuildHeadroom_HostsWithDifferentLimits_AreEachMeasuredAgainstTheirOwn()
    {
        // Same 10,000 calls/day on both hosts, but asia advertises half europe's ceiling
        // (50:120 -> 36,000/day): asia binds, at (36,000 - 10,000) / 100 = 260 accounts.
        var inputs = Inputs(
            Since,
            Route("europe", 70_000, AppLimit),
            Route("asia", 70_000, "10:1,50:120"));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.BindingRoute.Should().Be("asia");
        headroom.AdditionalAccountsHeadroom.Should().Be(260);
        headroom.Routes.Single(r => r.Route == "europe").AdditionalAccountsHeadroom.Should().Be(620);
    }

    [Fact]
    public void BuildHeadroom_SaturatedHost_ClampsSpareToZeroAndBindsAtZero()
    {
        // europe: 100,000/day against a 72,000/day ceiling — spare clamped, never negative.
        var inputs = Inputs(
            Since,
            Route("europe", 700_000, AppLimit),
            Route("euw1", 7_000, AppLimit));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.BindingRoute.Should().Be("europe");
        headroom.AdditionalAccountsHeadroom.Should().Be(0);
        headroom.Routes[0].SpareCallsPerDay.Should().Be(0);
    }

    [Fact]
    public void BuildHeadroom_ShortRouteKeyedHistory_MeasuresOverTheCoveredSpanOnly()
    {
        // Route-keyed rollups only go back 2 days: 20,000 calls are 10,000/day, not the
        // 2,857/day a division by the nominal 7 days would make of them.
        var inputs = Inputs(Now.AddDays(-2), Route("europe", 20_000, AppLimit));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.SufficientData.Should().BeTrue();
        headroom.ObservedWindowHours.Should().BeApproximately(48, 0.001);
        headroom.Routes[0].ObservedCallsPerDay.Should().BeApproximately(10_000, 0.001);
        headroom.AdditionalAccountsHeadroom.Should().Be(620);
    }

    [Fact]
    public void BuildHeadroom_UnknownAndUnlimitedHosts_NeverBind()
    {
        // "unknown" belongs to no budget: left out. A host without a limit header keeps its
        // measured cost but cannot bind.
        var inputs = Inputs(
            Since,
            Route("unknown", 7_000_000, AppLimit),
            Route("kr", 700_000, null),
            Route("euw1", 70_000, AppLimit));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(inputs, trackedAccounts: 100, Now);

        headroom.SufficientData.Should().BeTrue();
        headroom.BindingRoute.Should().Be("euw1");
        headroom.Routes.Select(r => r.Route).Should().Equal("euw1", "kr");
        var kr = headroom.Routes[1];
        kr.CallsPerAccountPerDay.Should().BeApproximately(1_000, 0.001);
        kr.BindingLimit.Should().BeNull();
        kr.SpareCallsPerDay.Should().BeNull();
        kr.AdditionalAccountsHeadroom.Should().BeNull();
    }

    [Fact]
    public void ResolveBindingLimit_MultiplePairs_PicksSmallestDailyCeiling()
    {
        // 1s window -> 1,728,000/day; 120s window -> 72,000/day (binding).
        var binding = RiotApiUsageQueryService.ResolveBindingLimit("20:1,100:120");

        binding.Should().NotBeNull();
        binding!.Limit.Should().Be(100);
        binding.WindowSeconds.Should().Be(120);
    }

    [Fact]
    public void ResolveBindingLimit_NullOrBlank_ReturnsNull()
    {
        RiotApiUsageQueryService.ResolveBindingLimit(null).Should().BeNull();
        RiotApiUsageQueryService.ResolveBindingLimit("   ").Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-pair")]
    [InlineData("20:")]
    [InlineData("20:0")]
    [InlineData("abc:120")]
    public void ResolveBindingLimit_MalformedPairs_AreSkipped(string malformed)
    {
        RiotApiUsageQueryService.ResolveBindingLimit(malformed).Should().BeNull();
    }

    [Fact]
    public void ResolveBindingLimit_MixOfValidAndMalformedPairs_UsesOnlyTheValidOnes()
    {
        var binding = RiotApiUsageQueryService.ResolveBindingLimit("20:1,bad:pair,100:120");

        binding.Should().NotBeNull();
        binding!.WindowSeconds.Should().Be(120);
    }

    private static RiotHeadroomInputs Inputs(DateTime? oldestRouteKeyed, params RiotRouteVolume[] routes)
        => new(Since, oldestRouteKeyed, routes);

    private static RiotRouteVolume Route(string route, long calls, string? appLimit)
        => new(route, calls, appLimit);
}
