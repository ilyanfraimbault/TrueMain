using System.Collections.Concurrent;
using System.Text.Json;

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
            item => item.id,
            item =>
            {
                var isBootsItem = IsBootsItem(item);
                return new ItemMetadata(
                    item.id,
                    item.priceTotal,
                    item.inStore,
                    ContainsCategory(item.categories, "Consumable"),
                    isBootsItem,
                    item.id == 1001,
                    item.to is null || item.to.Count == 0,
                    (item.to is null || item.to.Count == 0)
                        && isBootsItem
                        && item.id != 1001);
            });
    }

    private static bool ContainsCategory(IReadOnlyCollection<string>? categories, string value)
        => categories?.Any(category => string.Equals(category, value, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsBootsItem(CommunityDragonItem item)
        => ContainsCategory(item.categories, "Boots")
           || (item.from?.Any(TierTwoBootIds.Contains) ?? false)
           || string.Equals(item.requiredBuffCurrencyName, "Feats_NoxianBootPurchaseBuff", StringComparison.OrdinalIgnoreCase)
           || string.Equals(item.requiredBuffCurrencyName, "Feats_SpecialQuestBootBuff", StringComparison.OrdinalIgnoreCase);

    public sealed class CommunityDragonItem
    {
        public int id { get; set; }
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public bool active { get; set; }
        public bool inStore { get; set; }
        public List<int> from { get; set; } = [];
        public List<int> to { get; set; } = [];
        public List<string> categories { get; set; } = [];
        public int maxStacks { get; set; }
        public string requiredChampion { get; set; } = string.Empty;
        public string requiredAlly { get; set; } = string.Empty;
        public string requiredBuffCurrencyName { get; set; } = string.Empty;
        public int requiredBuffCurrencyCost { get; set; }
        public int specialRecipe { get; set; }
        public bool isEnchantment { get; set; }
        public int price { get; set; }
        public int priceTotal { get; set; }
        public bool displayInItemSets { get; set; }
        public string iconPath { get; set; } = string.Empty;
    }
}
