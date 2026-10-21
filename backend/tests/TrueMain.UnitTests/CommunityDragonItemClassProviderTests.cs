using System.Net;
using System.Text;
using AwesomeAssertions;
using Data.Statics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace TrueMain.UnitTests;

/// <summary>
/// Drives <see cref="CommunityDragonItemClassProvider"/> through a fake handler: the class bits
/// read off each <c>ItemData</c> entry in the data's order, the patch-to-branch mapping, the
/// <c>latest</c> fallback and its re-probe, the per-patch cache, and eviction of a faulted load.
/// </summary>
public sealed class CommunityDragonItemClassProviderTests
{
    private const string Bin = """
        {
          "Items/3031": { "itemID": 3031, "mItemAttributes": [2], "__type": "ItemData" },
          "Items/3153": { "itemID": 3153, "mItemAttributes": [1, 2], "__type": "ItemData" },
          "Items/3190": { "itemID": 3190, "mItemAttributes": [32, 8], "__type": "ItemData" },
          "Items/3026": { "itemID": 3026, "mItemAttributes": [1, 4, 2, 1], "__type": "ItemData" },
          "Items/3877": { "itemID": 3877, "mItemAttributes": [], "__type": "ItemData" },
          "Items/1001": { "itemID": 1001, "__type": "ItemData" },
          "Items/9999": { "itemID": 9999, "mItemAttributes": [64], "__type": "ItemData" },
          "Items/ItemGroups/Boots": { "mItemGroupID": "Boots", "__type": "ItemGroup" },
          "{0fe5f3f2}": { "mAttribute": "Lethality", "__type": "ItemAdviceAttribute" }
        }
        """;

    [Fact]
    public async Task GetClassesAsync_ReadsEachItemsClassBits_InTheDataOrder_AndSkipsTheRest()
    {
        using var handler = new FakeHandler();
        var provider = CreateProvider(handler, new FakeTimeProvider());

        var answer = await provider.GetClassesAsync("16.20.1", CancellationToken.None);

        answer.Patch.Should().Be("16.20");
        answer.Items.Keys.Should().BeEquivalentTo([3031, 3153, 3190, 3026], "an item with no known class bit is left out");
        answer.Items[3031].Should().Equal(ItemShopClass.Marksman);
        answer.Items[3153].Should().Equal(ItemShopClass.Fighter, ItemShopClass.Marksman);
        answer.Items[3190].Should().Equal([ItemShopClass.Support, ItemShopClass.Tank], "the main class comes first");
        answer.Items[3026].Should().Equal([ItemShopClass.Fighter, ItemShopClass.Assassin, ItemShopClass.Marksman], "a repeated bit counts once");
        handler.Requested.Should().ContainSingle().Which.Should().Be("https://raw.communitydragon.org/16.20/game/items.cdtb.bin.json");

        await provider.GetClassesAsync("16.20.3", CancellationToken.None);
        handler.Requested.Should().HaveCount(1, "a patch is read once");
    }

    [Fact]
    public async Task GetClassesAsync_ReadsLatest_WhenNoPatchIsAsked()
    {
        using var handler = new FakeHandler();
        var provider = CreateProvider(handler, new FakeTimeProvider());

        var answer = await provider.GetClassesAsync(null, CancellationToken.None);

        answer.Patch.Should().Be("latest");
        handler.Requested.Should().ContainSingle().Which.Should().Contain("/latest/");
    }

    [Fact]
    public async Task GetClassesAsync_FallsBackToLatest_AndProbesThePatchAgainLater()
    {
        using var handler = new FakeHandler { Unpublished = "16.21" };
        var clock = new FakeTimeProvider();
        var provider = CreateProvider(handler, clock);

        var answer = await provider.GetClassesAsync("16.21.1", CancellationToken.None);
        answer.Patch.Should().Be("latest");
        handler.Requested.Should().HaveCount(2);

        await provider.GetClassesAsync("16.21.1", CancellationToken.None);
        handler.Requested.Should().HaveCount(2, "the fallback stands for a while");

        handler.Unpublished = null;
        clock.Advance(TimeSpan.FromMinutes(31));
        var later = await provider.GetClassesAsync("16.21.1", CancellationToken.None);
        later.Patch.Should().Be("16.21");
    }

    [Fact]
    public async Task GetClassesAsync_RetriesAfterAFailedLoad()
    {
        using var handler = new FakeHandler { FailOnce = true };
        var provider = CreateProvider(handler, new FakeTimeProvider());

        var failing = () => provider.GetClassesAsync("16.20.1", CancellationToken.None);
        await failing.Should().ThrowAsync<HttpRequestException>();

        var answer = await provider.GetClassesAsync("16.20.1", CancellationToken.None);
        answer.Items.Should().NotBeEmpty();
    }

    private static CommunityDragonItemClassProvider CreateProvider(FakeHandler handler, TimeProvider clock)
        => new(() => new HttpClient(handler, disposeHandler: false), NullLogger<CommunityDragonItemClassProvider>.Instance, clock);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        public string? Unpublished { get; set; }

        public bool FailOnce { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            if (FailOnce)
            {
                FailOnce = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            if (Unpublished is not null && url.Contains($"/{Unpublished}/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Bin, Encoding.UTF8, "application/json"),
            });
        }
    }
}
