using System.Net;
using System.Text;
using AwesomeAssertions;
using Data.Statics;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.UnitTests;

/// <summary>
/// Drives <see cref="DataDragonChampionStaticsProvider"/> through a fake handler: the
/// patch-to-version mapping, the newest-version fallback when Data Dragon has not
/// published the patch, the per-patch cache, and eviction of a faulted load so the
/// next call retries.
/// </summary>
public sealed class DataDragonChampionStaticsProviderTests
{
    private const string Versions = """["16.17.1", "16.16.1", "16.15.1"]""";

    private const string Champions = """
        {
          "type": "champion",
          "version": "16.17.1",
          "data": {
            "Aatrox": { "id": "Aatrox", "key": "266", "stats": { "attackrange": 175, "hp": 650 } },
            "Caitlyn": { "id": "Caitlyn", "key": "51", "stats": { "attackrange": 650, "hp": 580 } },
            "Gnar": { "id": "Gnar", "key": "150", "stats": { "attackrange": 400, "hp": 540 } },
            "Broken": { "id": "Broken", "key": "not-a-number", "stats": { "attackrange": 100 } }
          }
        }
        """;

    [Fact]
    public async Task GetChampionsAsync_MapsThePatchOntoItsPublishedVersion_AndReadsKeyAndRange()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var champions = await provider.GetChampionsAsync("16.16.712.4321", CancellationToken.None);

        champions.Should().HaveCount(3, "a champion whose key does not parse is skipped");
        champions[266].Should().Be(new ChampionStatics(266, "Aatrox", 175));
        champions[51].AttackRange.Should().Be(650);
        champions[150].AttackRange.Should().Be(400);
        handler.Requested.Should().Contain(url => url.Contains("/cdn/16.16.1/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetChampionsAsync_FallsBackToTheNewestVersion_WhenThePatchIsNotPublishedYet()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        await provider.GetChampionsAsync("16.18.1.1", CancellationToken.None);

        handler.Requested.Should().Contain(url => url.Contains("/cdn/16.17.1/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetChampionsAsync_LoadsOncePerPatch()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        await provider.GetChampionsAsync("16.16.1.1", CancellationToken.None);
        await provider.GetChampionsAsync("16.16.9.9", CancellationToken.None);

        handler.Requested.Count(url => url.EndsWith("champion.json", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task GetChampionsAsync_RetriesAfterAFailedLoad()
    {
        using var handler = new FakeHandler { FailVersionsOnce = true };
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var act = () => provider.GetChampionsAsync("16.16.1.1", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();

        var champions = await provider.GetChampionsAsync("16.16.1.1", CancellationToken.None);
        champions.Should().NotBeEmpty("the faulted load was evicted rather than cached");
    }

    private static DataDragonChampionStaticsProvider CreateProvider(HttpClient client)
        => new(client, NullLogger<DataDragonChampionStaticsProvider>.Instance);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        public bool FailVersionsOnce { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            if (url.EndsWith("versions.json", StringComparison.Ordinal))
            {
                if (FailVersionsOnce)
                {
                    FailVersionsOnce = false;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                return Task.FromResult(Json(Versions));
            }

            if (url.EndsWith("champion.json", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(Champions));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
