using System.Collections.Frozen;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Lol.Items;
using Core.Lol.Patches;
using Data.Statics;
using Microsoft.Extensions.Logging;

namespace Data.BuildFacts;

/// <summary>
/// Reads the client's item metadata (<c>items.json</c>) from CommunityDragon, through the shared
/// per-patch cache (<see cref="PerPatchStaticCache{T}"/>).
/// </summary>
/// <remarks>
/// CommunityDragon mirrors a patch hours-to-days after Riot ships it, so on every patch day the
/// first games on the new patch reach aggregation while <c>/&lt;patch&gt;/</c> still 404s; its
/// <c>latest</c> branch answers them — item ids are stable across patches, and treating the 404
/// as fatal aborted the pattern fold over a handful of new-patch rows (#1107).
/// </remarks>
public sealed class CommunityDragonItemMetadataProvider : IItemMetadataProvider
{
    /// <summary>
    /// CommunityDragon's branch of last resort. It always exists and tracks the
    /// newest game data it has published, so it is the previous patch while a
    /// freshly shipped patch is still missing, and becomes that patch as soon as
    /// CommunityDragon catches up.
    /// </summary>
    private const string LatestBranch = "latest";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlySet<int> TierTwoBootIds = LolItemIds.TierTwoBoots.All;

    private readonly Func<HttpClient> _createClient;
    private readonly ILogger<CommunityDragonItemMetadataProvider> _logger;
    private readonly PerPatchStaticCache<IReadOnlyDictionary<int, ItemMetadata>> _cache;

    public CommunityDragonItemMetadataProvider(
        Func<HttpClient> createClient,
        ILogger<CommunityDragonItemMetadataProvider> logger,
        TimeProvider timeProvider)
    {
        _createClient = createClient;
        _logger = logger;
        _cache = new(
            "Item metadata",
            LoadAsync,
            async ct => await LoadAsync(LatestBranch, ct)
                ?? throw new InvalidOperationException($"CommunityDragon does not serve its '{LatestBranch}' branch."),
            logger,
            timeProvider);
    }

    public Task<StaticData<IReadOnlyDictionary<int, ItemMetadata>>> GetItemsAsync(string gameVersion, CancellationToken ct)
        => PatchVersion.TryParse(gameVersion, out var patch)
            ? _cache.GetAsync(patch.ToMajorMinor(), ct)
            : Task.FromResult(StaticData<IReadOnlyDictionary<int, ItemMetadata>>.Unavailable(
                $"'{gameVersion}' is not a patch."));

    private async Task<IReadOnlyDictionary<int, ItemMetadata>?> LoadAsync(string branch, CancellationToken ct)
    {
        if (await TryFetchBranchAsync(branch, ct) is not { } items)
        {
            return null;
        }

        _logger.LogInformation("Loaded {Count} item metadata rows for {Branch}.", items.Count, branch);

        var supportFamily = DetectSupportQuestFamily(items);
        if (supportFamily.RootId > 0)
        {
            _logger.LogInformation(
                "Detected support-quest family for {Branch}: root={RootId}, intermediates={IntermediateCount}, completions={CompletionCount}.",
                branch, supportFamily.RootId, supportFamily.IntermediateIds.Count, supportFamily.CompletionIds.Count);
        }

        var metadata = items.ToDictionary(
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
                    item.Id == LolItemIds.BootsOfSpeed,
                    to.Count == 0,
                    to.Count == 0
                        && isBootsItem
                        && item.Id != LolItemIds.BootsOfSpeed)
                {
                    IsInventoryTransformItem = IsInventoryTransformItem(item),
                    TransformFromItemId = item.SpecialRecipe > 0 ? item.SpecialRecipe : null,
                    IsSupportQuestStarter = supportFamily.IsRoot(item.Id),
                    IsSupportQuestIntermediate = supportFamily.IsIntermediate(item.Id),
                    IsSupportQuestCompletion = supportFamily.IsCompletion(item.Id),
                    IsStarterClassItem = IsStarterClassItem(item, isBootsItem),
                    Categories = categories.ToFrozenSet(StringComparer.Ordinal),
                    GrantsGrievousWounds = item.Description.Contains("Grievous Wounds", StringComparison.OrdinalIgnoreCase),
                    Components = [.. item.From ?? []],
                };
            });

        return metadata;
    }

    private async Task<List<CommunityDragonItem>?> TryFetchBranchAsync(string branch, CancellationToken ct)
    {
        var url = $"https://raw.communitydragon.org/{branch}/plugins/rcp-be-lol-game-data/global/default/v1/items.json";
        using var httpClient = _createClient();
        using var response = await httpClient.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<List<CommunityDragonItem>>(stream, JsonOptions, ct) ?? [];
    }

    /// <summary>
    /// A patch's resolved item metadata, plus whether it actually came from that
    /// patch's branch and when it was loaded — the two facts
    /// <see cref="GetItemsAsync"/> needs to decide when to re-probe.
    /// </summary>
    /// <summary>
    /// Detect the support-quest item family for a given patch, 100% from
    /// metadata. Riot publishes a stable internal marker
    /// (<see cref="LolItemIds.RequiredBuffCurrency.SupportItemPurchase"/>) on
    /// the single in-store root of the chain. From there we walk the
    /// <c>specialRecipe</c> graph forward to collect transitional items, and
    /// pick up the leaves (back-in-store completions like Bloodsong / Solstice
    /// Sleigh / ...) by checking their <c>from</c> arrays.
    ///
    /// No hardcoded IDs anywhere — if Riot rebuilds the system in a future
    /// patch this re-runs against the new metadata and returns the new family
    /// (or <see cref="SupportQuestFamily.Empty"/> if the marker is missing).
    /// </summary>
    internal static SupportQuestFamily DetectSupportQuestFamily(
        IReadOnlyList<CommunityDragonItem> items)
    {
        var roots = items
            .Where(item =>
                string.Equals(
                    item.RequiredBuffCurrencyName,
                    LolItemIds.RequiredBuffCurrency.SupportItemPurchase,
                    StringComparison.Ordinal)
                && item.InStore
                && (item.From ?? []).Count == 0)
            .ToList();

        // Exactly one root is expected. Zero means an old or post-rework patch
        // where this detection doesn't apply (graceful fallback). More than
        // one would be a Riot data oddity we don't want to silently misclassify
        // — bail and let the existing inventory-transform heuristic handle it.
        if (roots.Count != 1)
        {
            return SupportQuestFamily.Empty;
        }

        var rootId = roots[0].Id;

        var intermediates = new HashSet<int>();
        var frontier = new HashSet<int> { rootId };
        while (frontier.Count > 0)
        {
            var nextFrontier = new HashSet<int>();
            foreach (var item in items)
            {
                if (item.InStore
                    || item.SpecialRecipe <= 0
                    || !frontier.Contains(item.SpecialRecipe)
                    || intermediates.Contains(item.Id)
                    || item.Id == rootId)
                {
                    continue;
                }
                intermediates.Add(item.Id);
                nextFrontier.Add(item.Id);
            }
            frontier = nextFrontier;
        }

        var completions = new HashSet<int>();
        foreach (var item in items)
        {
            if (!item.InStore || item.Id == rootId)
            {
                continue;
            }
            var from = item.From ?? [];
            if (from.Count == 0)
            {
                continue;
            }
            if (from.Any(intermediates.Contains))
            {
                completions.Add(item.Id);
            }
        }

        return new SupportQuestFamily(rootId, intermediates, completions);
    }

    internal sealed record SupportQuestFamily(
        int RootId,
        IReadOnlySet<int> IntermediateIds,
        IReadOnlySet<int> CompletionIds)
    {
        public static SupportQuestFamily Empty { get; } =
            new(0, new HashSet<int>(), new HashSet<int>());

        public bool IsRoot(int itemId) => RootId > 0 && itemId == RootId;

        public bool IsIntermediate(int itemId) => IntermediateIds.Contains(itemId);

        public bool IsCompletion(int itemId) => CompletionIds.Contains(itemId);
    }

    private static bool ContainsCategory(IReadOnlyCollection<string> categories, string value)
        => categories.Any(category => string.Equals(category, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsBootsItem(CommunityDragonItem item)
        => ContainsCategory(item.Categories ?? [], "Boots")
           || (item.From ?? []).Any(TierTwoBootIds.Contains)
           || string.Equals(item.RequiredBuffCurrencyName, LolItemIds.RequiredBuffCurrency.FeatsNoxianBootPurchase, StringComparison.OrdinalIgnoreCase)
           || string.Equals(item.RequiredBuffCurrencyName, LolItemIds.RequiredBuffCurrency.FeatsSpecialQuestBoot, StringComparison.OrdinalIgnoreCase);

    private static bool IsInventoryTransformItem(CommunityDragonItem item)
        => !item.InStore
           && item.SpecialRecipe > 0
           && (item.To ?? []).Count == 0
           && item.PriceTotal >= 2_000;

    /// <summary>
    /// Detect "starter-class" items: those Riot tags with the Lane or Jungle
    /// semantic category and that match the structural shape of a starter
    /// purchase (in-store, no recipe, no upgrade, cheap, non-consumable,
    /// non-boots). Catches Doran's, Cull, jungle pets, ARAM Guardian's, and
    /// the support-quest root in a single pass — no hardcoded IDs. Items
    /// matching this rule must never appear in <c>BuildItem0..6</c>.
    /// </summary>
    private static bool IsStarterClassItem(CommunityDragonItem item, bool isBootsItem)
    {
        if (!item.InStore)
        {
            return false;
        }
        if ((item.From ?? []).Count > 0)
        {
            return false;
        }
        if ((item.To ?? []).Count > 0)
        {
            return false;
        }
        if (item.PriceTotal <= 0 || item.PriceTotal >= 600)
        {
            return false;
        }
        var categories = item.Categories ?? [];
        if (ContainsCategory(categories, "Consumable"))
        {
            return false;
        }
        if (isBootsItem)
        {
            return false;
        }
        return ContainsCategory(categories, "Lane")
               || ContainsCategory(categories, "Jungle");
    }

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
