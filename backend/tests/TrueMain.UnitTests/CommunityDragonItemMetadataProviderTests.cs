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

    [Fact]
    public async Task GetItemsAsync_should_flag_support_quest_family_for_world_atlas_chain()
    {
        // Patch-16.10-shaped payload: one in-store root with the
        // SupportItemPurchaseBuff marker, two not-in-store transitional
        // items linked by specialRecipe, two in-store completions whose
        // `from` points at the second transitional item.
        const string payload = """
[
  {
    "id": 3865, "name": "World Atlas", "description": "", "active": false,
    "inStore": true, "from": [], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "SupportItemPurchaseBuff",
    "requiredBuffCurrencyCost": 0, "specialRecipe": 0,
    "isEnchantment": false, "price": 400, "priceTotal": 400,
    "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 3866, "name": "Runic Compass", "description": "", "active": true,
    "inStore": false, "from": [], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 3865, "isEnchantment": false, "price": 400,
    "priceTotal": 400, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 3867, "name": "Bounty of Worlds", "description": "", "active": true,
    "inStore": false, "from": [], "to": [3869, 3877], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 3866, "isEnchantment": false, "price": 400,
    "priceTotal": 400, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 3869, "name": "Celestial Opposition", "description": "", "active": true,
    "inStore": true, "from": [3867], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "S11Support_Quest_Completion_Buff",
    "requiredBuffCurrencyCost": 0, "specialRecipe": 0,
    "isEnchantment": false, "price": 0, "priceTotal": 400,
    "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 3877, "name": "Bloodsong", "description": "", "active": true,
    "inStore": true, "from": [3867], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "S11Support_Quest_Completion_Buff",
    "requiredBuffCurrencyCost": 0, "specialRecipe": 0,
    "isEnchantment": false, "price": 0, "priceTotal": 400,
    "displayInItemSets": true, "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("16.10.1", CancellationToken.None);

        items[3865].IsSupportQuestStarter.Should().BeTrue();
        items[3865].IsSupportQuestIntermediate.Should().BeFalse();
        items[3865].IsSupportQuestCompletion.Should().BeFalse();

        items[3866].IsSupportQuestIntermediate.Should().BeTrue();
        items[3867].IsSupportQuestIntermediate.Should().BeTrue();

        items[3869].IsSupportQuestCompletion.Should().BeTrue();
        items[3877].IsSupportQuestCompletion.Should().BeTrue();

        items[3869].IsSupportQuestStarter.Should().BeFalse();
        items[3869].IsSupportQuestIntermediate.Should().BeFalse();
    }

    [Fact]
    public async Task GetItemsAsync_should_leave_all_support_quest_flags_false_when_marker_is_absent()
    {
        // Older patches (before 15.10) don't carry the SupportItemPurchaseBuff
        // marker — detection must gracefully fall back to all-false on each
        // item so the rest of the pipeline keeps the legacy behaviour.
        const string payload = """
[
  {
    "id": 3850, "name": "Spellthief's Edge (legacy)", "description": "", "active": false,
    "inStore": true, "from": [], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 400,
    "priceTotal": 400, "displayInItemSets": true, "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("14.20.1", CancellationToken.None);

        items[3850].IsSupportQuestStarter.Should().BeFalse();
        items[3850].IsSupportQuestIntermediate.Should().BeFalse();
        items[3850].IsSupportQuestCompletion.Should().BeFalse();
    }

    [Fact]
    public async Task GetItemsAsync_should_skip_detection_when_multiple_candidate_roots_exist()
    {
        // Safety net: if Riot data ever has more than one in-store item with
        // the SupportItemPurchaseBuff marker, we don't try to guess which
        // chain is "the" chain — bail out and let the existing pipeline
        // handle it without family flags.
        const string payload = """
[
  {
    "id": 3865, "name": "World Atlas A", "description": "", "active": false,
    "inStore": true, "from": [], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "SupportItemPurchaseBuff",
    "requiredBuffCurrencyCost": 0, "specialRecipe": 0,
    "isEnchantment": false, "price": 400, "priceTotal": 400,
    "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 3868, "name": "World Atlas B", "description": "", "active": false,
    "inStore": true, "from": [], "to": [], "categories": [],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "SupportItemPurchaseBuff",
    "requiredBuffCurrencyCost": 0, "specialRecipe": 0,
    "isEnchantment": false, "price": 400, "priceTotal": 400,
    "displayInItemSets": true, "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("99.99.1", CancellationToken.None);

        items[3865].IsSupportQuestStarter.Should().BeFalse();
        items[3868].IsSupportQuestStarter.Should().BeFalse();
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
