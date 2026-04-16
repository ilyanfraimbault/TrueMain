using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class CommunityDragonItemMetadataProvider(
    HttpClient httpClient,
    ILogger<CommunityDragonItemMetadataProvider> logger) : IItemMetadataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<int> TierTwoBootIds = [3006, 3047, 3111, 3020, 3158, 3117, 3009, 3013];
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<int, ItemMetadata>>>> _cache = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
    {
        var patch = ChampionPatternNormalization.NormalizePatchVersion(gameVersion);
        var lazyTask = _cache.GetOrAdd(patch, static (normalizedPatch, provider) =>
            new Lazy<Task<IReadOnlyDictionary<int, ItemMetadata>>>(
                () => provider.LoadPatchItemsAsync(normalizedPatch, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);

        return lazyTask.Value.WaitAsync(ct);
    }

    private async Task<IReadOnlyDictionary<int, ItemMetadata>> LoadPatchItemsAsync(string patch, CancellationToken ct)
    {
        var url = $"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/items.json";
        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var items = await JsonSerializer.DeserializeAsync<List<CommunityDragonItem>>(stream, JsonOptions, ct) ?? [];

        logger.LogInformation("Loaded {Count} item metadata rows for patch {Patch}.", items.Count, patch);

        return items.ToDictionary(
            item => item.Id,
            item =>
            {
                var categories = item.Categories ?? [];
                var to = item.To ?? [];
                var isBootsItem = IsBootsItem(item);
                return new ItemMetadata(
                    item.Id,
                    item.PriceTotal,
                    item.InStore,
                    ContainsCategory(categories, "Consumable"),
                    isBootsItem,
                    item.Id == 1001,
                    to.Count == 0,
                    to.Count == 0
                        && isBootsItem
                        && item.Id != 1001)
                {
                    IsInventoryTransformItem = IsInventoryTransformItem(item),
                    TransformFromItemId = item.SpecialRecipe > 0 ? item.SpecialRecipe : null
                };
            });
    }

    private static bool ContainsCategory(IReadOnlyCollection<string> categories, string value)
        => categories.Any(category => string.Equals(category, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsBootsItem(CommunityDragonItem item)
        => ContainsCategory(item.Categories ?? [], "Boots")
           || (item.From ?? []).Any(TierTwoBootIds.Contains)
           || string.Equals(item.RequiredBuffCurrencyName, "Feats_NoxianBootPurchaseBuff", StringComparison.OrdinalIgnoreCase)
           || string.Equals(item.RequiredBuffCurrencyName, "Feats_SpecialQuestBootBuff", StringComparison.OrdinalIgnoreCase);

    private static bool IsInventoryTransformItem(CommunityDragonItem item)
        => !item.InStore
           && item.SpecialRecipe > 0
           && (item.To ?? []).Count == 0
           && item.PriceTotal >= 2_000;

    public sealed class CommunityDragonItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("inStore")]
        public bool InStore { get; set; }

        [JsonPropertyName("from")]
        public List<int>? From { get; set; }

        [JsonPropertyName("to")]
        public List<int>? To { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("maxStacks")]
        public int MaxStacks { get; set; }

        [JsonPropertyName("requiredChampion")]
        public string RequiredChampion { get; set; } = string.Empty;

        [JsonPropertyName("requiredAlly")]
        public string RequiredAlly { get; set; } = string.Empty;

        [JsonPropertyName("requiredBuffCurrencyName")]
        public string RequiredBuffCurrencyName { get; set; } = string.Empty;

        [JsonPropertyName("requiredBuffCurrencyCost")]
        public int RequiredBuffCurrencyCost { get; set; }

        [JsonPropertyName("specialRecipe")]
        public int SpecialRecipe { get; set; }

        [JsonPropertyName("isEnchantment")]
        public bool IsEnchantment { get; set; }

        [JsonPropertyName("price")]
        public int Price { get; set; }

        [JsonPropertyName("priceTotal")]
        public int PriceTotal { get; set; }

        [JsonPropertyName("displayInItemSets")]
        public bool DisplayInItemSets { get; set; }

        [JsonPropertyName("iconPath")]
        public string IconPath { get; set; } = string.Empty;
    }
}
