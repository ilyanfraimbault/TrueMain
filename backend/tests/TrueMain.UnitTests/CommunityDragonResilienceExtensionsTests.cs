using System.Net;
using AwesomeAssertions;
using Ingestor.BuildFacts;
using Ingestor.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

public sealed class CommunityDragonResilienceExtensionsTests
{
    [Fact]
    public void AddCommunityDragonResilienceHandler_AppliesConfiguredRetryAndTimeouts()
    {
        var options = ResolveConfiguredOptions("cdragon-configured", communityDragon =>
        {
            communityDragon.MaxRetryAttempts = 2;
            communityDragon.AttemptTimeoutSeconds = 20;
            communityDragon.TotalRequestTimeoutSeconds = 120;
        });

        options.Retry.MaxRetryAttempts.Should().Be(2);
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(20));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void AddCommunityDragonResilienceHandler_DefaultsFitAMetadataFetch()
    {
        var options = ResolveConfiguredOptions("cdragon-defaults", _ => { });

        // CommunityDragon serves a multi-megabyte static JSON file, so the attempt timeout
        // is more generous than the standard handler's 10s default, and the total covers
        // every attempt plus the exponential backoff waits between them.
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(75));
        options.Retry.MaxRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void AddCommunityDragonResilienceHandler_ClampsAttemptTimeoutToFitTheTotalBudget()
    {
        var options = ResolveConfiguredOptions("cdragon-invariants", communityDragon =>
        {
            communityDragon.MaxRetryAttempts = 3;
            communityDragon.AttemptTimeoutSeconds = 30;
            communityDragon.TotalRequestTimeoutSeconds = 60;
        });

        // The total is a hard ceiling here (unlike the Riot handler, which raises it to fit
        // Riot's Retry-After waits): 4 attempts must fit inside 60s, so each gets 15s. The
        // total is left untouched so it can never outgrow the caller's HttpClient.Timeout.
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AddCommunityDragonResilienceHandler_KeepsCircuitBreakerSamplingValid()
    {
        var options = ResolveConfiguredOptions("cdragon-sampling", communityDragon =>
        {
            communityDragon.MaxRetryAttempts = 1;
            communityDragon.AttemptTimeoutSeconds = 60;
            communityDragon.TotalRequestTimeoutSeconds = 240;
        });

        // The standard handler validates SamplingDuration >= 2 x attempt timeout; the
        // handler raises it from the 30s default so validation keeps passing.
        options.CircuitBreaker.SamplingDuration.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task AddCommunityDragonResilienceHandler_RetriesTransientFailures()
    {
        const string clientName = "cdragon-retry";

        var services = new ServiceCollection();
        services.Configure<CommunityDragonOptions>(_ => { });

        // The factory also disposes the handler it was handed; HttpMessageHandler.Dispose
        // is a no-op here, so the extra `using` costs nothing and satisfies CA2000.
        using var primaryHandler = new SequenceHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.BadGateway,
            HttpStatusCode.OK);

        services.AddHttpClient(clientName)
            .AddCommunityDragonResilienceHandler()
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        // Registered after the handler so it wins: the standard strategy's exponential
        // backoff would otherwise sleep seconds between attempts and slow the suite down.
        services.Configure<HttpStandardResilienceOptions>(
            $"{clientName}-standard",
            options => options.Retry.Delay = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        using var response = await client.GetAsync(
            "https://raw.communitydragon.org/15.1/plugins/rcp-be-lol-game-data/global/default/v1/items.json",
            CancellationToken.None);

        // Two transient failures are retried away instead of surfacing to the caller —
        // which is the whole point of putting the handler on this client.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        primaryHandler.Attempts.Should().Be(3);
    }

    private static HttpStandardResilienceOptions ResolveConfiguredOptions(
        string clientName,
        Action<CommunityDragonOptions> configure)
    {
        var services = new ServiceCollection();
        services.Configure(configure);

        services.AddHttpClient(clientName).AddCommunityDragonResilienceHandler();

        using var provider = services.BuildServiceProvider();

        // The standard handler stores its options under "{clientName}-standard" — the
        // pipeline name seen in ops logs. Get() also runs the handler's own validators, so
        // an invariant-breaking configuration would fail this resolution.
        return provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{clientName}-standard");
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statusCodes) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _attempts) - 1;
            var statusCode = statusCodes[Math.Min(index, statusCodes.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
