using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Riot.RateLimiting;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace TrueMain.UnitTests;

/// <summary>
/// The per-caller half of the limiter (#1457). A permanent match-fetch worker runs on the
/// regional routes all the time, and the cadenced processes that also need them —
/// AccountRefresh, ManualSeed — must keep completing their calls beside it: their reserved
/// share is held back from everyone else while they are active, handed back as soon as they
/// are not, and the worker itself is held to its target rate.
/// </summary>
public sealed class RiotRateLimiterReservationTests
{
    private const string Worker = "MatchIngestion";
    private const string Refresh = "AccountRefresh";

    private static readonly DateTimeOffset Origin = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    [Fact]
    public void AcquireAsync_ReservedCallerActive_HoldsItsUnusedShareBackFromTheWorker()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0.25, appLimits: "20:120");

        // AccountRefresh has spent one of the five permits it reserves; four stay held.
        IssueImmediately(limiter, callers, Refresh, "europe");

        for (var i = 0; i < 15; i++)
        {
            IssueImmediately(limiter, callers, Worker, "europe");
        }

        StartAcquisition(limiter, callers, Worker, "europe").IsCompleted
            .Should().BeFalse("16 of 20 permits are spent and the other four are AccountRefresh's");

        // The reservation is AccountRefresh's to use, immediately, however busy the worker is.
        for (var i = 0; i < 4; i++)
        {
            IssueImmediately(limiter, callers, Refresh, "europe");
        }
    }

    [Fact]
    public void AcquireAsync_ReservedCallerIdle_LetsTheWorkerUseTheWholeWindow()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0.25, appLimits: "20:120");

        // An idle reservation holds nothing: its share goes to whoever can use it.
        for (var i = 0; i < 20; i++)
        {
            IssueImmediately(limiter, callers, Worker, "europe");
        }
    }

    [Fact]
    public async Task AcquireAsync_ReservationLapses_HandsTheShareBackWithoutWaitingOutTheWindow()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0.25, appLimits: "20:120");

        IssueImmediately(limiter, callers, Refresh, "europe");
        for (var i = 0; i < 15; i++)
        {
            IssueImmediately(limiter, callers, Worker, "europe");
        }

        var held = StartAcquisition(limiter, callers, Worker, "europe");
        held.IsCompleted.Should().BeFalse();

        // AccountRefresh's run ended: past the linger it is idle and its share is free. The
        // window itself only frees a permit at 120 s, so a worker that slept until then
        // would leave the share unused for two minutes.
        time.Advance(TimeSpan.FromSeconds(6));
        await held.WaitAsync(Settle);
    }

    [Fact]
    public async Task AcquireAsync_UnderSustainedWorkerLoad_GivesTheReservedCallerTheNextFreedPermit()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0.25, appLimits: "4:10");

        // The worker fills the window while AccountRefresh is idle.
        for (var i = 0; i < 4; i++)
        {
            IssueImmediately(limiter, callers, Worker, "europe");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        // Both now want the route. Without the reservation the next permit would go to
        // whichever woke first, and a worker with several fetches in flight wins that race
        // nearly every time — which is how a permanent worker starves a cadenced process.
        var refresh = StartAcquisition(limiter, callers, Refresh, "europe");
        var worker = StartAcquisition(limiter, callers, Worker, "europe");

        time.Advance(TimeSpan.FromSeconds(6));
        await refresh.WaitAsync(Settle);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        worker.IsCompleted.Should().BeFalse("the freed permit was AccountRefresh's, and it has just spent it");

        time.Advance(TimeSpan.FromSeconds(1));
        await worker.WaitAsync(Settle);
    }

    [Fact]
    public void AcquireAsync_OnAPlatformRoute_HoldsNoReservation()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0.25, appLimits: "4:120");

        // The reservations exist for the routes the workers live on; a platform route keeps
        // the plain first-come budget it always had.
        IssueImmediately(limiter, callers, Refresh, "euw1");
        for (var i = 0; i < 3; i++)
        {
            IssueImmediately(limiter, callers, "LadderSync", "euw1");
        }
    }

    [Fact]
    public async Task AcquireAsync_CallerCeiling_HoldsOnlyThatCallerToItsTargetRate()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0, appLimits: "100:120");
        limiter.SetCallerCeiling(Worker, 2);

        IssueImmediately(limiter, callers, Worker, "europe");
        IssueImmediately(limiter, callers, Worker, "europe");

        var third = StartAcquisition(limiter, callers, Worker, "europe");
        third.IsCompleted.Should().BeFalse("the worker's target is two calls a minute");

        // The ceiling is the worker's alone: the route still has budget for everyone else.
        IssueImmediately(limiter, callers, Refresh, "europe");

        time.Advance(TimeSpan.FromMinutes(1));
        await third.WaitAsync(Settle);
    }

    [Fact]
    public void SetCallerCeiling_Null_LiftsTheCeiling()
    {
        var time = new FakeTimeProvider(Origin);
        var callers = new CallerContext();
        using var limiter = CreateLimiter(time, callers, reservedShare: 0, appLimits: "100:120");
        limiter.SetCallerCeiling(Worker, 1);
        limiter.SetCallerCeiling(Worker, null);

        IssueImmediately(limiter, callers, Worker, "europe");
        IssueImmediately(limiter, callers, Worker, "europe");
    }

    [Theory]
    [InlineData(0.05, 0.02, true)]
    [InlineData(0.6, 0, false)]
    [InlineData(0.3, 0.3, false)]
    [InlineData(-0.1, 0, false)]
    public void HasBoundedReservations_ChecksEachShareAndTheirSum(double refresh, double seed, bool expected)
    {
        RiotRateLimitOptionsValidation
            .HasBoundedReservations(new Dictionary<string, double> { [Refresh] = refresh, ["ManualSeed"] = seed })
            .Should().Be(expected);
    }

    private static void IssueImmediately(IRiotRateLimiter limiter, ICallerContext callers, string caller, string routingValue)
    {
        StartAcquisition(limiter, callers, caller, routingValue).IsCompletedSuccessfully.Should().BeTrue(
            $"{caller} had a permit available on {routingValue} and must not have been delayed");
    }

    private static Task StartAcquisition(IRiotRateLimiter limiter, ICallerContext callers, string caller, string routingValue)
    {
        // The caller is ambient, as it is in production: the Worker opens it around each run.
        // The acquisition captures it as it starts, so the scope can close before it ends.
        var scope = callers.BeginCall(caller);
        var acquisition = limiter.AcquireAsync(routingValue, "match-v5.match", CancellationToken.None).AsTask();
        scope.Dispose();
        return acquisition;
    }

    private static RiotRateLimiter CreateLimiter(
        TimeProvider timeProvider,
        ICallerContext callers,
        double reservedShare,
        string appLimits)
    {
        var options = new RiotRateLimitOptions
        {
            AppLimits = appLimits,
            SafetyHeadroom = 0,
            EnforceMethodLimits = false,
            ReservedShares = new Dictionary<string, double>(StringComparer.Ordinal) { [Refresh] = reservedShare },
            ReservationLingerSeconds = 5
        };

        return new RiotRateLimiter(
            Microsoft.Extensions.Options.Options.Create(options),
            timeProvider,
            TestIngestorMetrics.Create(),
            callers,
            NullLogger<RiotRateLimiter>.Instance);
    }
}
