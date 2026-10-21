using System.Net;
using System.Text;
using AwesomeAssertions;
using Data.Statics;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.UnitTests;

/// <summary>
/// Drives <see cref="DataDragonItemStatsProvider"/> (#1750) through a fake handler: the stats
/// read off <c>item.json</c>, the patch-to-version mapping and its newest-version fallback, the
/// per-patch cache, and eviction of a faulted load so the next call retries.
/// </summary>
public sealed class DataDragonItemStatsProviderTests
{
    private const string Versions = """["16.21.1", "16.20.1"]""";

    private const string Items = """
        {
          "type": "item",
          "data": {
            "3075": { "name": "Thornmail", "stats": { "FlatHPPoolMod": 150, "FlatArmorMod": 75 }, "tags": ["Health", "Armor"] },
            "4633": { "name": "Riftmaker", "stats": { "FlatMagicDamageMod": 70 }, "tags": ["SpellDamage", "SpellVamp"] },
            "3031": { "name": "Infinity Edge", "stats": { "FlatCritChanceMod": 0.25, "FlatPhysicalDamageMod": 75 } },
            "3153": { "name": "Blade of The Ruined King", "stats": { "FlatPhysicalDamageMod": 40, "PercentAttackSpeedMod": 0.25, "PercentLifeStealMod": 0.1 } },
            "3065": { "name": "Spirit Visage", "stats": { "FlatSpellBlockMod": 50 }, "tags": ["SpellBlock"] },
            "junk": { "stats": {} }
          }
        }
        """;

    [Fact]
    public async Task GetItemsAsync_ReadsEachStatTheAxesSum()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var items = await provider.GetItemsAsync("16.20.712.4321", CancellationToken.None);

        items.Keys.Should().BeEquivalentTo([3075, 4633, 3031, 3153, 3065], "an id that does not parse is skipped");
        items[3075].Armor.Should().Be(75);
        items[3065].MagicResist.Should().Be(50);
        items[4633].AbilityPower.Should().Be(70);
        items[4633].Tags.Should().Contain("SpellVamp", "omnivamp has no stat key, only a tag");
        items[3031].CritChance.Should().Be(0.25);
        items[3031].AttackDamage.Should().Be(75);
        items[3031].Tags.Should().BeEmpty();
        items[3153].AttackSpeed.Should().Be(0.25);
        items[3153].LifeSteal.Should().Be(0.1);
        handler.Requested.Should().Contain(url => url.Contains("/cdn/16.20.1/data/en_US/item.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetItemsAsync_FallsBackToTheNewestVersion_AndLoadsOncePerPatch()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        await provider.GetItemsAsync("16.22.1.1", CancellationToken.None);
        await provider.GetItemsAsync("16.22.5.5", CancellationToken.None);

        handler.Requested.Should().Contain(url => url.Contains("/cdn/16.21.1/", StringComparison.Ordinal));
        handler.Requested.Count(url => url.EndsWith("item.json", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task GetItemsAsync_RetriesAfterAFailedLoad()
    {
        using var handler = new FakeHandler { FailVersionsOnce = true };
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var act = () => provider.GetItemsAsync("16.20.1.1", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();

        (await provider.GetItemsAsync("16.20.1.1", CancellationToken.None))
            .Should().NotBeEmpty("the faulted load was evicted rather than cached");
    }

    private static DataDragonItemStatsProvider CreateProvider(HttpClient client)
        => new(client, NullLogger<DataDragonItemStatsProvider>.Instance);

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

            return Task.FromResult(url.EndsWith("item.json", StringComparison.Ordinal)
                ? Json(Items)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
