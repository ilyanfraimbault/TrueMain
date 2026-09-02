using System.Net;
using System.Text;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Ingestor.Riot;

namespace TrueMain.UnitTests;

/// <summary>
/// The match-ids URL is the whole point of #1358: every id this call returns that we cannot
/// store is a <c>/matches/{id}</c> we pay for on this claim and on every later claim of the same
/// account, for ever — nothing is written for a discarded match, so it reads as new every time.
/// These tests pin the query string, not the deserialization (that is
/// <see cref="RiotJsonSourceGenerationTests"/>).
/// </summary>
public sealed class RiotMatchIdQueryTests
{
    [Fact]
    public async Task GetMatchIdsAsync_SendsQueueAndStartTime_AlongsideTypeRanked()
    {
        // 2026-09-01T12:00:00Z.
        var startTimeUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var expectedEpochSeconds = new DateTimeOffset(startTimeUtc).ToUnixTimeSeconds();

        var uri = await CaptureUriAsync(new MatchIdQuery("puuid-a", RegionalRoute.Asia, 20, 420, startTimeUtc));

        uri.Should().Contain("/lol/match/v5/matches/by-puuid/puuid-a/ids");
        uri.Should().Contain("count=20");
        // Riot ANDs type and queue, so flex (440) never reaches the per-match fetch.
        uri.Should().Contain("type=ranked").And.Contain("queue=420");
        uri.Should().Contain($"startTime={expectedEpochSeconds}");
    }

    [Fact]
    public async Task GetMatchIdsAsync_OmitsQueueAndStartTime_WhenUnset()
    {
        // A first ingestion has no previous ingest to bound the window with, and the queue
        // stays optional so a caller that tracks no single queue is not forced to invent one.
        var uri = await CaptureUriAsync(new MatchIdQuery("puuid-a", RegionalRoute.Europe, 20));

        uri.Should().Contain("type=ranked");
        uri.Should().NotContain("queue=");
        uri.Should().NotContain("startTime=");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(100, 100)]
    // Riot 400s above 100, so a larger MatchesPerAccount must not reach the wire verbatim.
    [InlineData(250, 100)]
    public async Task GetMatchIdsAsync_ClampsCountToRiotsRange(int requested, int expected)
    {
        var uri = await CaptureUriAsync(new MatchIdQuery("puuid-a", RegionalRoute.Americas, requested));

        uri.Should().Contain($"count={expected}");
    }

    private static async Task<string> CaptureUriAsync(MatchIdQuery query)
    {
        using var handler = new CapturingHandler("""["KR_1"]""");
        using var httpClient = new HttpClient(handler);

        await new RiotMatchClient(httpClient).GetMatchIdsAsync(query, CancellationToken.None);

        handler.LastUri.Should().NotBeNull();
        return handler.LastUri!.ToString();
    }

    private sealed class CapturingHandler(string payload) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
