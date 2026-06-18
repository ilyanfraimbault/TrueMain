using System.Net;
using System.Text;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Ingestor.Riot;

namespace TrueMain.UnitTests;

public sealed class RiotAccountClientByPuuidTests
{
    [Fact]
    public async Task GetAccountByPuuidAsync_ShouldHitByPuuidOnTheRegionalHostAndDeserialise()
    {
        const string payload = """
        { "puuid": "puuid-1", "gameName": "Phantasm", "tagLine": "EUW1" }
        """;
        using var handler = new RecordingHandler(HttpStatusCode.OK, payload);
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        var account = await client.GetAccountByPuuidAsync("puuid-1", RegionalRoute.Europe, CancellationToken.None);

        account.Should().NotBeNull();
        account.Puuid.Should().Be("puuid-1");
        account.GameName.Should().Be("Phantasm");
        account.TagLine.Should().Be("EUW1");

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.Host.Should().Be("europe.api.riotgames.com");
        handler.LastRequestUri.AbsolutePath
            .Should().Be("/riot/account/v1/accounts/by-puuid/puuid-1");
    }

    [Fact]
    public async Task GetAccountByPuuidAsync_ShouldThrowInvalidOperation_WhenBodyIsJsonNull()
    {
        // A literal `null` body deserialises to a null reference; the streaming
        // helper turns that into an explicit InvalidOperationException rather than
        // handing a null DTO downstream.
        using var handler = new RecordingHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        var act = async () => await client.GetAccountByPuuidAsync("puuid-1", RegionalRoute.Europe, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAccountByPuuidAsync_ShouldThrow_OnFailureStatus()
    {
        using var handler = new RecordingHandler(HttpStatusCode.TooManyRequests, string.Empty);
        using var httpClient = new HttpClient(handler);
        var client = new RiotAccountClient(httpClient);

        var act = async () => await client.GetAccountByPuuidAsync("puuid-1", RegionalRoute.Europe, CancellationToken.None);

        // 429/5xx are transport problems that must surface to the resilience layer.
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
