using System.Net;
using System.Text;
using FluentAssertions;
using Ingestor.Processes.Components.PatternAggregation;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.UnitTests;

public sealed class CommunityDragonItemMetadataProviderTests
{
    [Fact]
    public async Task GetItemsAsync_ShouldClassifyTierThreeMidlaneBootsAsBoots()
    {
        const string payload = """
[
  {
    "id": 3172,
    "name": "Gunmetal Greaves",
    "description": "",
    "active": true,
    "inStore": true,
    "from": [3006],
    "to": [],
    "categories": ["AttackSpeed", "LifeSteal", "NonbootsMovement"],
    "maxStacks": 1,
    "requiredChampion": "",
    "requiredAlly": "",
    "requiredBuffCurrencyName": "Feats_NoxianBootPurchaseBuff",
    "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0,
    "isEnchantment": false,
    "price": 0,
    "priceTotal": 1100,
    "displayInItemSets": true,
    "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("16.7.1", CancellationToken.None);

        items[3172].IsBootsItem.Should().BeTrue();
        items[3172].IsFinalBoots.Should().BeTrue();
    }

    [Fact]
    public async Task GetItemsAsync_ShouldFlagMuramanaStyleTransformsAsInventoryTransformItems()
    {
        const string payload = """
[
  {
    "id": 3042,
    "name": "Muramana",
    "description": "",
    "active": true,
    "inStore": false,
    "from": [],
    "to": [],
    "categories": ["Damage", "Mana"],
    "maxStacks": 1,
    "requiredChampion": "",
    "requiredAlly": "",
    "requiredBuffCurrencyName": "",
    "requiredBuffCurrencyCost": 0,
    "specialRecipe": 3004,
    "isEnchantment": false,
    "price": 0,
    "priceTotal": 2900,
    "displayInItemSets": true,
    "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("16.7.1", CancellationToken.None);

        items[3042].IsInventoryTransformItem.Should().BeTrue();
        items[3042].TransformFromItemId.Should().Be(3004);
    }

    private sealed class StubHttpMessageHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
    }
}
