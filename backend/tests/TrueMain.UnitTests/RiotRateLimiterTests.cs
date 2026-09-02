using System.Net;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Riot.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace TrueMain.UnitTests;

/// <summary>
/// The Riot budget is enforced per routing value (#1359), so these tests pin the two
/// properties the pipeline's throughput rests on: a routing value never issues more than
/// its advertised limit, and two routing values never wait on each other.
/// </summary>
public sealed class RiotRateLimiterTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Acquire_IssuesUpToTheConfiguredLimitWithoutWaiting()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "3:1";
            options.SafetyHeadroom = 0;
        });

        // Asserted as synchronous completion rather than awaited: a regression that made
        // these wait would otherwise hang the suite on a fake clock nobody advances.
        for (var i = 0; i < 3; i++)
        {
            IssueImmediately(limiter, "europe", "match-v5.match");
        }
    }

    [Fact]
    public async Task Acquire_HoldsTheCallThatWouldExceedTheWindowUntilItSlides()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "2:1";
            options.SafetyHeadroom = 0;
        });

        IssueImmediately(limiter, "europe", "match-v5.match");
        IssueImmediately(limiter, "europe", "match-v5.match");

        var third = limiter.AcquireAsync("europe", "match-v5.match", CancellationToken.None).AsTask();
        third.IsCompleted.Should().BeFalse("the window is full until the first permit expires");

        time.Advance(TimeSpan.FromSeconds(1));
        await third;
    }

    [Fact]
    public async Task Acquire_KeepsRoutingValuesIndependent()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "1:120";
            options.SafetyHeadroom = 0;
        });

        IssueImmediately(limiter, "europe", "match-v5.match");

        // This is the whole point of the change: `europe` being exhausted for the next two
        // minutes must not stop `americas` from sending immediately.
        IssueImmediately(limiter, "americas", "match-v5.match");

        var europe = limiter.AcquireAsync("europe", "match-v5.match", CancellationToken.None).AsTask();
        europe.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(120));
        await europe;
    }

    [Fact]
    public void Observe_AdoptsTheLimitsRiotAdvertises()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "1:1";
            options.SafetyHeadroom = 0;
        });

        // A production key advertises a much larger budget; the limiter must widen to it
        // without a redeploy.
        using var response = Response(HttpStatusCode.OK, ("X-App-Rate-Limit", "500:10"));
        limiter.Observe("europe", "match-v5.match", response);

        for (var i = 0; i < 5; i++)
        {
            IssueImmediately(limiter, "europe", "match-v5.match");
        }

        // The seeded guess is replaced, not merged: a leftover "1 per second" would still
        // be the binding constraint behind a key that allows 500 per 10 seconds.
        limiter.Snapshot().Single().Windows.Should().Equal([(500, TimeSpan.FromSeconds(10))]);
    }

    [Fact]
    public async Task Observe_SyncsTheCountRiotReportsSoAnUnderCountBecomesAWait()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "10:120";
            options.SafetyHeadroom = 0;
        });

        // Riot says nine of the ten permits are already spent — by a caller this limiter
        // never saw. Only one more may go out before the window slides.
        using var response = Response(
            HttpStatusCode.OK,
            ("X-App-Rate-Limit", "10:120"),
            ("X-App-Rate-Limit-Count", "9:120"));
        limiter.Observe("europe", "match-v5.match", response);

        IssueImmediately(limiter, "europe", "match-v5.match");

        var blocked = limiter.AcquireAsync("europe", "match-v5.match", CancellationToken.None).AsTask();
        blocked.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(120));
        await blocked;
    }

    [Fact]
    public async Task Observe_AppliesRetryAfterToTheWholeRoutingValueOnAnApplicationLimit()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options => options.AppLimits = "100:1");

        using var response = Response(
            HttpStatusCode.TooManyRequests,
            ("X-Rate-Limit-Type", "application"),
            ("Retry-After", "30"));
        limiter.Observe("europe", "match-v5.match", response);

        // A different endpoint on the same routing value is blocked too: the exhausted
        // budget is the routing value's, not the endpoint's.
        var other = limiter.AcquireAsync("europe", "account-v1.byPuuid", CancellationToken.None).AsTask();
        other.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(30));
        await other;
    }

    [Fact]
    public async Task Observe_AppliesAMethodLimitOnlyToThatEndpoint()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options => options.AppLimits = "100:1");

        using var response = Response(
            HttpStatusCode.TooManyRequests,
            ("X-Rate-Limit-Type", "method"),
            ("Retry-After", "10"));
        limiter.Observe("europe", "match-v5.timeline", response);

        IssueImmediately(limiter, "europe", "match-v5.match");

        var throttled = limiter.AcquireAsync("europe", "match-v5.timeline", CancellationToken.None).AsTask();
        throttled.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(10));
        await throttled;
    }

    [Fact]
    public async Task Observe_TreatsAnUnattributed429AsARoutingValuePenalty()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "100:1";
            options.DefaultRetryAfterSeconds = 7;
        });

        // A service-level throttle carries no type and often no Retry-After. Guessing
        // narrowly would keep hammering whatever is actually exhausted.
        using var response = Response(HttpStatusCode.TooManyRequests);
        limiter.Observe("kr", "league-v4.master", response);

        var blocked = limiter.AcquireAsync("kr", "summoner-v4.byPuuid", CancellationToken.None).AsTask();
        blocked.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(7));
        await blocked;
    }

    [Fact]
    public void Acquire_DoesNothingWhenDisabled()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.Enabled = false;
            options.AppLimits = "1:120";
        });

        IssueImmediately(limiter, "europe", "match-v5.match");
        IssueImmediately(limiter, "europe", "match-v5.match");

        limiter.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_HoldsBackTheConfiguredSafetyHeadroom()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "10:1";
            options.SafetyHeadroom = 0.2;
        });

        // 20% of ten permits is held back, so the ninth call waits for the window.
        for (var i = 0; i < 8; i++)
        {
            IssueImmediately(limiter, "europe", "match-v5.match");
        }

        var ninth = limiter.AcquireAsync("europe", "match-v5.match", CancellationToken.None).AsTask();
        ninth.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(1));
        await ninth;
    }

    [Fact]
    public void Acquire_NeverStarvesABudgetWhoseLimitIsOne()
    {
        var time = new FakeTimeProvider(Origin);
        using var limiter = CreateLimiter(time, options =>
        {
            options.AppLimits = "1:1";
            // Headroom would round a single-permit window down to zero, which would block
            // the routing value for ever; the floor of one permit is what prevents it.
            options.SafetyHeadroom = 0.4;
        });

        IssueImmediately(limiter, "europe", "match-v5.match");
    }

    /// <summary>
    /// Takes a permit that must be available now. Asserting synchronous completion keeps a
    /// regression visible as a failing test instead of a suite that hangs on a fake clock.
    /// </summary>
    private static void IssueImmediately(IRiotRateLimiter limiter, string routingValue, string endpoint)
    {
        var acquisition = limiter.AcquireAsync(routingValue, endpoint, CancellationToken.None).AsTask();

        acquisition.IsCompletedSuccessfully.Should().BeTrue(
            $"{routingValue}/{endpoint} had a permit available and must not have been delayed");
    }

    private static RiotRateLimiter CreateLimiter(TimeProvider timeProvider, Action<RiotRateLimitOptions> configure)
    {
        var options = new RiotRateLimitOptions();
        configure(options);
        return new RiotRateLimiter(
            Microsoft.Extensions.Options.Options.Create(options),
            timeProvider,
            TestIngestorMetrics.Create(),
            NullLogger<RiotRateLimiter>.Instance);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(statusCode);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
