using System.Net;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// The handler is the only thing that connects the limiter to a real call, so what it
/// has to get right is the mapping: the routing value and endpoint it charges must be
/// the ones the request actually targets, and the response must flow back into the
/// limiter so Riot's own headers steer it.
/// </summary>
public sealed class RiotRateLimitHandlerTests
{
    [Fact]
    public async Task SendAsync_ChargesTheRoutingValueAndEndpointOfTheRequest()
    {
        var limiter = new RecordingRateLimiter();
        using var inner = new StubHandler(HttpStatusCode.OK);
        using var handler = new RiotRateLimitHandler(limiter) { InnerHandler = inner };
        using var client = new HttpClient(handler, disposeHandler: false);

        using var response = await client.GetAsync(
            new Uri("https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_1/timeline"));

        limiter.Acquired.Should().ContainSingle().Which.Should().Be(("europe", "match-v5.timeline"));
    }

    [Fact]
    public async Task SendAsync_ChargesThePlatformRoutingValueSeparatelyFromTheRegionalOne()
    {
        var limiter = new RecordingRateLimiter();
        using var inner = new StubHandler(HttpStatusCode.OK);
        using var handler = new RiotRateLimitHandler(limiter) { InnerHandler = inner };
        using var client = new HttpClient(handler, disposeHandler: false);

        using var response = await client.GetAsync(
            new Uri("https://euw1.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/abc"));

        limiter.Acquired.Should().ContainSingle().Which.Should().Be(("euw1", "summoner-v4.byPuuid"));
    }

    [Fact]
    public async Task ObserverHandler_FeedsA429BackIntoTheLimiter()
    {
        var limiter = new RecordingRateLimiter();
        using var inner = new StubHandler(HttpStatusCode.TooManyRequests);
        using var handler = new RiotRateLimitObserverHandler(limiter) { InnerHandler = inner };
        using var client = new HttpClient(handler, disposeHandler: false);

        using var response = await client.GetAsync(
            new Uri("https://americas.api.riotgames.com/lol/match/v5/matches/NA1_1"));

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limiter.Observed.Should().ContainSingle().Which.Should().Be(("americas", "match-v5.match"));
    }

    /// <summary>
    /// The failure that #1359's first deploy hit on preprod within the hour: the permit wait
    /// sat inside the resilience handler, so a legitimate wait on a 100-per-2-minutes window
    /// was charged to the 10-second per-attempt timeout, the attempt was cancelled on the
    /// queue rather than on the network, and the account's whole ingestion failed. The wait
    /// now happens outside the resilience pipeline, so a wait longer than the attempt timeout
    /// is simply a wait.
    /// </summary>
    [Fact]
    public async Task WaitingLongerThanTheAttemptTimeout_DoesNotFailTheRequest()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRiotRateLimiter>(new SlowRateLimiter(TimeSpan.FromSeconds(2)));
        services.AddTransient<RiotRateLimitHandler>();
        services.AddSingleton<IOptions<RiotOptions>>(Microsoft.Extensions.Options.Options.Create(new RiotOptions
        {
            ApiKey = "k",
            MaxRetryAttempts = 1,
            AttemptTimeoutSeconds = 1,
            TotalRequestTimeoutSeconds = 30
        }));

        services.AddHttpClient("riot")
            .AddHttpMessageHandler<RiotRateLimitHandler>()
            .AddRiotResilienceHandler()
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("riot");
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.GetAsync(
            new Uri("https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class SlowRateLimiter(TimeSpan wait) : IRiotRateLimiter
    {
        public async ValueTask AcquireAsync(string routingValue, string endpoint, CancellationToken cancellationToken)
            => await Task.Delay(wait, cancellationToken);

        public void Observe(string routingValue, string endpoint, HttpResponseMessage response)
        {
        }

        public IReadOnlyList<RiotRateLimitSnapshot> Snapshot() => [];
    }

    private sealed class RecordingRateLimiter : IRiotRateLimiter
    {
        public List<(string RoutingValue, string Endpoint)> Acquired { get; } = [];

        public List<(string RoutingValue, string Endpoint)> Observed { get; } = [];

        public ValueTask AcquireAsync(string routingValue, string endpoint, CancellationToken cancellationToken)
        {
            Acquired.Add((routingValue, endpoint));
            return ValueTask.CompletedTask;
        }

        public void Observe(string routingValue, string endpoint, HttpResponseMessage response)
            => Observed.Add((routingValue, endpoint));

        public IReadOnlyList<RiotRateLimitSnapshot> Snapshot() => [];
    }

    // Builds a fresh response per call so ownership passes to the caller's `using`,
    // rather than handing the same instance out twice.
    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
