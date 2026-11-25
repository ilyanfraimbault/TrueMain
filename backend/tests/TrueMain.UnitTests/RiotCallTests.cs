using System.Net;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Metrics.Mongo;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.RateLimiting;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Every Riot request carries the endpoint its client method names (#93, #1359) — the key of
/// its method bucket and its usage rollup — instead of having it re-derived from the URL.
/// </summary>
public sealed class RiotCallTests
{
    public static TheoryData<string, Func<HttpClient, Task>, string, string, string> ClientCalls => new()
    {
        { "match", http => new RiotMatchClient(http).GetMatchAsync("EUW1_1", RegionalRoute.Europe, default), "match-v5.match", "europe", "/lol/match/v5/matches/EUW1_1" },
        { "timeline", http => new RiotMatchClient(http).GetTimelineAsync("EUW1_1", RegionalRoute.Europe, default), "match-v5.timeline", "europe", "/lol/match/v5/matches/EUW1_1/timeline" },
        { "match ids", http => new RiotMatchClient(http).GetMatchIdsAsync(new MatchIdQuery("P", RegionalRoute.Americas, 20), default), "match-v5.matchIdsByPuuid", "americas", "/lol/match/v5/matches/by-puuid/P/ids" },
        { "challenger", http => new RiotPlatformClient(http).GetChallengerLeagueAsync(PlatformRoute.EUW1, "RANKED_SOLO_5x5", default), "league-v4.challenger", "euw1", "/lol/league/v4/challengerleagues/by-queue/RANKED_SOLO_5x5" },
        { "grandmaster", http => new RiotPlatformClient(http).GetGrandmasterLeagueAsync(PlatformRoute.KR, "RANKED_SOLO_5x5", default), "league-v4.grandmaster", "kr", "/lol/league/v4/grandmasterleagues/by-queue/RANKED_SOLO_5x5" },
        { "master", http => new RiotPlatformClient(http).GetMasterLeagueAsync(PlatformRoute.NA1, "RANKED_SOLO_5x5", default), "league-v4.master", "na1", "/lol/league/v4/masterleagues/by-queue/RANKED_SOLO_5x5" },
        { "summoner", http => new RiotPlatformClient(http).GetSummonerAsync(PlatformRoute.EUW1, "S", default), "summoner-v4.byId", "euw1", "/lol/summoner/v4/summoners/S" },
        { "summoner by puuid", http => new RiotPlatformClient(http).GetSummonerByPuuidAsync(PlatformRoute.EUW1, "P", default), "summoner-v4.byPuuid", "euw1", "/lol/summoner/v4/summoners/by-puuid/P" },
        { "masteries", http => new RiotPlatformClient(http).GetChampionMasteriesAsync(PlatformRoute.EUW1, "P", default), "championMastery-v4.byPuuid", "euw1", "/lol/champion-mastery/v4/champion-masteries/by-puuid/P" },
        { "entries by puuid", http => new RiotPlatformClient(http).GetLeagueEntriesByPuuidAsync(PlatformRoute.EUW1, "P", default), "league-v4.entriesByPuuid", "euw1", "/lol/league/v4/entries/by-puuid/P" },
        { "entries by division", http => new RiotPlatformClient(http).GetLeagueEntriesAsync(PlatformRoute.EUW1, "RANKED_SOLO_5x5", "DIAMOND", "I", 2, default), "league-v4.entriesByDivision", "euw1", "/lol/league/v4/entries/RANKED_SOLO_5x5/DIAMOND/I" },
        { "account by puuid", http => new RiotAccountClient(http).GetAccountByPuuidAsync("P", RegionalRoute.Asia, default), "account-v1.byPuuid", "asia", "/riot/account/v1/accounts/by-puuid/P" },
        { "account by riot id", http => new RiotAccountClient(http).GetByRiotIdAsync("Name", "Tag", RegionalRoute.Sea, default), "account-v1.byRiotId", "sea", "/riot/account/v1/accounts/by-riot-id/Name/Tag" },
    };

    [Theory]
    [MemberData(nameof(ClientCalls))]
    public async Task EveryClientMethod_AttachesItsEndpointAndRoutingValue(
        string label,
        Func<HttpClient, Task> call,
        string method,
        string routingValue,
        string path)
    {
        using var capture = new Capture();
        using var http = new HttpClient(capture, disposeHandler: false);

        var act = () => call(http);
        await act.Should().ThrowAsync<CapturedException>(label);

        capture.Calls.Should().ContainSingle().Which.Should().Be(new RiotCall(new RiotEndpoint(method), routingValue));
        capture.Uris.Single().Host.Should().Be($"{routingValue}.api.riotgames.com");
        capture.Uris.Single().AbsolutePath.Should().Be(path);
    }

    [Fact]
    public void A_RequestNotBuiltForRiot_CarriesNoCall()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_1");

        RiotCall.Of(request).Should().BeNull();
    }

    [Fact]
    public async Task TheCall_SurvivesAResilienceRetry_OnTheSharedPipeline()
    {
        // The rate limiter outside the resilience handler and the observer and metrics inside
        // it read the same call; a retried attempt is a new request message built from a
        // snapshot, which must keep the options.
        var recorder = Substitute.For<IRiotApiCallRecorder>();
        using var attempts = new Capture(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IRiotRateLimiter>());
        services.AddSingleton(recorder);
        services.AddSingleton(Substitute.For<ICallerContext>());
        services.AddOptions<RiotOptions>().Configure(riot => riot.MaxRetryAttempts = 1);
        services.AddOptions<RiotRateLimitOptions>();
        services.AddRiotClient<IRiotMatchClient, RiotMatchClient>().ConfigurePrimaryHttpMessageHandler(() => new NonDisposing(attempts));
        await using var provider = services.BuildServiceProvider();

        var ids = await provider.GetRequiredService<IRiotMatchClient>()
            .GetMatchIdsAsync(new MatchIdQuery("P", RegionalRoute.Europe, 5), CancellationToken.None);

        ids.Should().BeEmpty();
        attempts.Calls.Should().HaveCount(2).And.AllBeEquivalentTo(new RiotCall(RiotEndpoint.MatchIdsByPuuid, "europe"));
        recorder.ReceivedCalls().Should().HaveCount(2, "the metrics handler records every physical attempt");
    }

    [Fact]
    public void TheClientTimeout_CoversThePipelineAndTheLongestPermitWait()
        => RiotClientServiceCollectionExtensions
            .ClientTimeout(new RiotOptions(), new RiotRateLimitOptions { MaxPermitWaitSeconds = 30 })
            .Should().Be(new RiotOptions().EffectiveTotalRequestTimeout() + TimeSpan.FromSeconds(35));

    private sealed class CapturedException : Exception;

    /// <summary>Lets the test, not the client factory, own the scripted handler.</summary>
    private sealed class NonDisposing(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing)
        {
            // The test disposes the inner handler itself.
        }
    }

    /// <summary>Records each request's call, then fails it — or answers the scripted statuses with an empty list.</summary>
    private sealed class Capture(params HttpStatusCode[] script) : HttpMessageHandler
    {
        public List<RiotCall?> Calls { get; } = [];

        public List<Uri> Uris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(RiotCall.Of(request));
            Uris.Add(request.RequestUri!);
            if (script.Length == 0)
            {
                throw new CapturedException();
            }

            var status = script[Math.Min(Calls.Count - 1, script.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("[]") });
        }
    }
}
