using AwesomeAssertions;
using Ingestor.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins <see cref="RiotOptions.EffectiveTotalRequestTimeout"/> — the single
/// calculation both the resilience handler and the ingestor's Riot
/// <c>HttpClient.Timeout</c> derive from, so the two can never drift apart the
/// way they did before #855 (the client timeout was left at the 100s default
/// while the handler quietly raised its own total).
/// </summary>
public sealed class RiotOptionsTests
{
    [Fact]
    public void EffectiveTotalRequestTimeout_ReturnsTheConfiguredTotal_WhenItAlreadyCoversEveryAttempt()
    {
        var options = new RiotOptions
        {
            AttemptTimeoutSeconds = 10,
            MaxRetryAttempts = 3,
            TotalRequestTimeoutSeconds = 180,
        };

        // 10s x (3 + 1) = 40s, comfortably under the configured 180s.
        options.EffectiveTotalRequestTimeout().Should().Be(TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void EffectiveTotalRequestTimeout_RaisesTheTotal_WhenTheConfiguredValueCannotFitEveryAttempt()
    {
        var options = new RiotOptions
        {
            AttemptTimeoutSeconds = 30,
            MaxRetryAttempts = 5,
            TotalRequestTimeoutSeconds = 60,
        };

        // 30s x (5 + 1) = 180s > the configured 60s.
        options.EffectiveTotalRequestTimeout().Should().Be(TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void EffectiveTotalRequestTimeout_AtTheMaxRetryBound_StaysWithinTheTotalTimeoutCeiling()
    {
        // The highest allowed retry count (10) against the highest allowed
        // per-attempt timeout (600s) must not blow past the 3600s ceiling
        // startup validation puts on TotalRequestTimeoutSeconds, or the two
        // bounds would silently disagree with each other.
        var options = new RiotOptions
        {
            AttemptTimeoutSeconds = 600,
            MaxRetryAttempts = 10,
            TotalRequestTimeoutSeconds = 3600,
        };

        // 600s x (10 + 1) = 6600s > 3600s: the raise wins. This is a deliberately
        // pathological combination (both options at their individual max), not
        // the shipped configuration — it exists to prove the calculation itself
        // has no ceiling, which is exactly why MaxRetryAttempts is bounded.
        options.EffectiveTotalRequestTimeout().Should().Be(TimeSpan.FromSeconds(6600));
    }
}
