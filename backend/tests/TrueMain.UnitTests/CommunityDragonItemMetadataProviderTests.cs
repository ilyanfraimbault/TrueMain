using System.Net;
using System.Text;
using AwesomeAssertions;
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

    [Fact]
    public async Task GetItemsAsync_should_flag_starter_class_items_by_lane_or_jungle_category()
    {
        // Patch-16.10-shaped payloads covering the three starter-class
        // families captured by the dynamic detector: a Doran's (Lane), Cull
        // (Lane farming item), and a jungle pet (Jungle). The detector keys
        // off the (Lane|Jungle) category combined with structural markers
        // (in-store, no recipe, no upgrade, cheap, non-consumable, non-boots).
        const string payload = """
[
  {
    "id": 1056, "name": "Doran's Ring", "description": "", "active": true,
    "inStore": true, "from": [], "to": [], "categories": ["Health", "Lane", "ManaRegen", "SpellDamage"],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 400,
    "priceTotal": 400, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 1083, "name": "Cull", "description": "", "active": true,
    "inStore": true, "from": [], "to": [], "categories": ["Damage", "OnHit", "Lane"],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 450,
    "priceTotal": 450, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 1101, "name": "Scorchclaw Pup", "description": "", "active": true,
    "inStore": true, "from": [], "to": [], "categories": ["Jungle"],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 450,
    "priceTotal": 450, "displayInItemSets": true, "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("16.10.1", CancellationToken.None);

        items[1056].IsStarterClassItem.Should().BeTrue();
        items[1083].IsStarterClassItem.Should().BeTrue();
        items[1101].IsStarterClassItem.Should().BeTrue();
    }

    [Fact]
    public async Task GetItemsAsync_should_not_flag_regular_build_items_as_starter_class()
    {
        // Discriminator must not catch items that share some structural
        // properties with starters but aren't (boots have the recipe of a
        // tier-2 transform, Liandry's costs >600, trinkets have no price,
        // a Long Sword has an upgrade chain).
        const string payload = """
[
  {
    "id": 1001, "name": "Boots", "description": "", "active": true,
    "inStore": true, "from": [], "to": [3006], "categories": ["Boots"],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 300,
    "priceTotal": 300, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 6653, "name": "Liandry's Anguish", "description": "", "active": true,
    "inStore": true, "from": [3916, 1058, 1052], "to": [],
    "categories": ["SpellDamage", "Health"],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 0,
    "priceTotal": 3000, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 3340, "name": "Stealth Ward", "description": "", "active": true,
    "inStore": true, "from": [], "to": [], "categories": ["Trinket"],
    "maxStacks": 1, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 0,
    "priceTotal": 0, "displayInItemSets": true, "iconPath": ""
  },
  {
    "id": 2003, "name": "Health Potion", "description": "", "active": true,
    "inStore": true, "from": [], "to": [], "categories": ["Consumable", "Lane"],
    "maxStacks": 5, "requiredChampion": "", "requiredAlly": "",
    "requiredBuffCurrencyName": "", "requiredBuffCurrencyCost": 0,
    "specialRecipe": 0, "isEnchantment": false, "price": 50,
    "priceTotal": 50, "displayInItemSets": true, "iconPath": ""
  }
]
""";

        using var handler = new StubHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var provider = new CommunityDragonItemMetadataProvider(httpClient, NullLogger<CommunityDragonItemMetadataProvider>.Instance);

        var items = await provider.GetItemsAsync("16.10.1", CancellationToken.None);

        items[1001].IsStarterClassItem.Should().BeFalse();
        items[6653].IsStarterClassItem.Should().BeFalse();
        items[3340].IsStarterClassItem.Should().BeFalse();
        items[2003].IsStarterClassItem.Should().BeFalse();
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
