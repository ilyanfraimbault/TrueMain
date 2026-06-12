using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Riot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

public sealed class RiotResilienceExtensionsTests
{
    [Fact]
    public void AddRiotResilienceHandler_AppliesConfiguredRetryAndTimeouts()
    {
        var options = ResolveConfiguredOptions("riot-configured", riot =>
        {
            riot.MaxRetryAttempts = 4;
            riot.AttemptTimeoutSeconds = 15;
            riot.TotalRequestTimeoutSeconds = 200;
        });

        options.Retry.MaxRetryAttempts.Should().Be(4);
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(200));
    }

    [Fact]
    public void AddRiotResilienceHandler_DefaultsSurviveRiotRateLimitBackoffs()
    {
        var options = ResolveConfiguredOptions("riot-defaults", _ => { });

        // Issue #443: the standard handler's 30s total (raised to 40s by the retry
        // invariant) starved every Discovery ladder page stuck behind a Riot 429
        // app-rate backoff, which can demand waits beyond 100s. The default total
        // must comfortably cover one such wait plus a successful attempt.
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(180));
        options.Retry.MaxRetryAttempts.Should().Be(3);

        // Those long waits only reach the total timeout because the standard retry
        // strategy honours Retry-After response headers; lock that behaviour in.
        options.Retry.ShouldRetryAfterHeader.Should().BeTrue();
    }

    [Fact]
    public void AddRiotResilienceHandler_KeepsTimeoutInvariants_WhenConfiguredTooLow()
    {
        var options = ResolveConfiguredOptions("riot-invariants", riot =>
        {
            riot.MaxRetryAttempts = 5;
            riot.AttemptTimeoutSeconds = 30;
            riot.TotalRequestTimeoutSeconds = 60;
        });

        // Total must cover every attempt: 30s x (5 + 1) = 180s.
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(180));

        // The standard handler validates SamplingDuration >= 2 x attempt timeout;
        // the handler raises it from the 30s default so validation keeps passing.
        options.CircuitBreaker.SamplingDuration.Should().Be(TimeSpan.FromSeconds(60));
    }

    private static HttpStandardResilienceOptions ResolveConfiguredOptions(
        string clientName,
        Action<RiotOptions> configure)
    {
        var services = new ServiceCollection();
        services.Configure<RiotOptions>(riot =>
        {
            riot.ApiKey = "test-key";
            configure(riot);
        });

        services.AddHttpClient(clientName).AddRiotResilienceHandler();

        using var provider = services.BuildServiceProvider();

        // The standard handler stores its options under "{clientName}-standard" —
        // the pipeline name seen in ops logs (e.g. 'IRiotPlatformClient-standard').
        // Get() also runs the handler's own validators, so an invariant-breaking
        // configuration would fail this resolution.
        return provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{clientName}-standard");
    }
}
