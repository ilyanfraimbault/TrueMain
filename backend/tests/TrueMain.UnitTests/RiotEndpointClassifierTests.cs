using AwesomeAssertions;
using Ingestor.Riot;

namespace TrueMain.UnitTests;

public sealed class RiotEndpointClassifierTests
{
    [Theory]
    // match-v5 — order-sensitive: ids and timeline must win over the bare match.
    [InlineData("https://europe.api.riotgames.com/lol/match/v5/matches/by-puuid/PUUID/ids?count=20&type=ranked", "match-v5.matchIdsByPuuid")]
    [InlineData("https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_123456/timeline", "match-v5.timeline")]
    [InlineData("https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_123456", "match-v5.match")]
    // account-v1
    [InlineData("https://europe.api.riotgames.com/riot/account/v1/accounts/by-puuid/PUUID", "account-v1.byPuuid")]
    [InlineData("https://europe.api.riotgames.com/riot/account/v1/accounts/by-riot-id/Name/Tag", "account-v1.byRiotId")]
    // league-v4
    [InlineData("https://euw1.api.riotgames.com/lol/league/v4/challengerleagues/by-queue/RANKED_SOLO_5x5", "league-v4.challenger")]
    [InlineData("https://euw1.api.riotgames.com/lol/league/v4/grandmasterleagues/by-queue/RANKED_SOLO_5x5", "league-v4.grandmaster")]
    [InlineData("https://euw1.api.riotgames.com/lol/league/v4/masterleagues/by-queue/RANKED_SOLO_5x5", "league-v4.master")]
    [InlineData("https://euw1.api.riotgames.com/lol/league/v4/entries/by-puuid/PUUID", "league-v4.entriesByPuuid")]
    // summoner-v4 — by-puuid must win over the bare summoner-by-id.
    [InlineData("https://euw1.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/PUUID", "summoner-v4.byPuuid")]
    [InlineData("https://euw1.api.riotgames.com/lol/summoner/v4/summoners/SUMMONER_ID", "summoner-v4.byId")]
    // champion-mastery-v4
    [InlineData("https://euw1.api.riotgames.com/lol/champion-mastery/v4/champion-masteries/by-puuid/PUUID", "championMastery-v4.byPuuid")]
    // unrecognised path falls back to "unknown" rather than throwing.
    [InlineData("https://europe.api.riotgames.com/lol/some/future/v9/endpoint", "unknown")]
    public void Classify_MapsKnownPathsToEndpointKeys(string url, string expectedEndpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var (endpoint, _) = RiotEndpointClassifier.Classify(request);

        endpoint.Should().Be(expectedEndpoint);
    }

    [Theory]
    [InlineData("https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_1", "europe")]
    [InlineData("https://euw1.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/PUUID", "euw1")]
    public void Classify_ExtractsRoutingHostLabel(string url, string expectedRoute)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var (_, route) = RiotEndpointClassifier.Classify(request);

        route.Should().Be(expectedRoute);
    }

    [Fact]
    public void Classify_WithoutRequestUri_ReturnsUnknownAndNullRoute()
    {
        using var request = new HttpRequestMessage { RequestUri = null };

        var (endpoint, route) = RiotEndpointClassifier.Classify(request);

        endpoint.Should().Be("unknown");
        route.Should().BeNull();
    }
}
