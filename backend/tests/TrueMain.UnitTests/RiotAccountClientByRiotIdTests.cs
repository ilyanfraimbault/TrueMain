using System.Net;
using System.Text;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Ingestor.Riot;

namespace TrueMain.UnitTests;

public sealed class RiotAccountClientByRiotIdTests
{
    [Fact]
    public async Task GetByRiotIdAsync_ShouldHitByRiotIdOnTheRegionalHostAndDeserialise()
    {
        const string payload = """
        { "puuid": "puuid-resolved-1", "gameName": "Phantasm", "tagLine": "EUW1" }
        """;
        using var handler = new RecordingHandler(HttpStatusCode.OK, payload);
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        var account = await client.GetByRiotIdAsync("Phantasm", "EUW1", RegionalRoute.Europe, CancellationToken.None);

        account.Should().NotBeNull();
        account!.Puuid.Should().Be("puuid-resolved-1");
        account.GameName.Should().Be("Phantasm");
        account.TagLine.Should().Be("EUW1");

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.Host.Should().Be("europe.api.riotgames.com");
        handler.LastRequestUri.AbsolutePath
            .Should().Be("/riot/account/v1/accounts/by-riot-id/Phantasm/EUW1");
    }

    [Fact]
    public async Task GetByRiotIdAsync_ShouldReturnNull_WhenRiotReports404()
    {
        using var handler = new RecordingHandler(HttpStatusCode.NotFound, """{ "status": { "message": "Data not found" } }""");
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        var account = await client.GetByRiotIdAsync("Ghost", "NA1", RegionalRoute.Americas, CancellationToken.None);

        account.Should().BeNull();
    }

    [Fact]
    public async Task GetByRiotIdAsync_ShouldUrlEncodeNameAndTag()
    {
        const string payload = """{ "puuid": "p", "gameName": "Some Player", "tagLine": "0001" }""";
        using var handler = new RecordingHandler(HttpStatusCode.OK, payload);
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        // A space in the game name must be percent-encoded so the Riot ID maps to
        // a valid path segment rather than splitting the URL.
        await client.GetByRiotIdAsync("Some Player", "0001", RegionalRoute.Europe, CancellationToken.None);

        handler.LastRequestUri.Should().NotBeNull();
        // AbsoluteUri preserves the raw (encoded) form; AbsolutePath would decode it.
        handler.LastRequestUri!.AbsoluteUri
            .Should().Be("https://europe.api.riotgames.com/riot/account/v1/accounts/by-riot-id/Some%20Player/0001");
    }

    [Fact]
    public async Task GetByRiotIdAsync_ShouldThrow_OnNon404Failure()
    {
        using var handler = new RecordingHandler(HttpStatusCode.TooManyRequests, string.Empty);
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        var act = async () => await client.GetByRiotIdAsync("Phantasm", "EUW1", RegionalRoute.Europe, CancellationToken.None);

        // 429/5xx etc. are transport problems, not "not found" — they must surface
        // rather than be swallowed as a null.
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string payload) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
