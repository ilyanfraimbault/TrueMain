using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Lol.Items;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.BuildFacts;

public sealed class CommunityDragonItemMetadataProvider(
    HttpClient httpClient,
    ILogger<CommunityDragonItemMetadataProvider> logger,
    TimeProvider timeProvider) : IItemMetadataProvider
{
    /// <summary>
    /// CommunityDragon's branch of last resort. It always exists and tracks the
    /// newest game data it has published, so it is the previous patch while a
    /// freshly shipped patch is still missing, and becomes that patch as soon as
    /// CommunityDragon catches up.
    /// </summary>
    private const string LatestBranch = "latest";

    /// <summary>
    /// How long a fallback stands before the real patch branch is probed again.
    /// Bounds how long a long-lived process (notably the Api, which runs for days)
    /// keeps reading a just-shipped patch through the previous patch's metadata.
    /// </summary>
    private static readonly TimeSpan FallbackRecheckInterval = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<int> TierTwoBootIds = LolItemIds.TierTwoBoots.All;
    private readonly ConcurrentDictionary<string, Lazy<Task<PatchItems>>> _cache = new(StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
    {
        var patch = PatchVersion.Parse(gameVersion).ToMajorMinor();

        // Two attempts at most: the second one runs against an entry this call just
        // created, which cannot itself be a stale fallback, so the loop terminates.
        for (var attempt = 0; ; attempt++)
        {
            var lazyTask = _cache.GetOrAdd(patch, static (normalizedPatch, provider) =>
                new Lazy<Task<PatchItems>>(
                    () => provider.LoadPatchItemsAsync(normalizedPatch, CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                this);

            PatchItems patchItems;
            try
            {
                patchItems = await lazyTask.Value.WaitAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A faulted load must not be remembered: the shared Lazy would keep
                // rethrowing the original failure for the life of the process, long
                // after the transient cause cleared.
                Evict(patch, lazyTask);
                throw;
            }

            if (!patchItems.IsFallback
                || attempt > 0
                || timeProvider.GetUtcNow() - patchItems.LoadedAtUtc < FallbackRecheckInterval)
            {
                return patchItems.Items;
            }

            // The fallback has stood long enough that CommunityDragon may have
            // published the real branch by now — drop it and load once more.
            Evict(patch, lazyTask);
        }
    }

    private void Evict(string patch, Lazy<Task<PatchItems>> entry)
        => ((ICollection<KeyValuePair<string, Lazy<Task<PatchItems>>>>)_cache)
            .Remove(new KeyValuePair<string, Lazy<Task<PatchItems>>>(patch, entry));

    private async Task<PatchItems> LoadPatchItemsAsync(string patch, CancellationToken ct)
    {
        var (items, isFallback) = await FetchPatchItemsAsync(patch, ct);

        logger.LogInformation("Loaded {Count} item metadata rows for patch {Patch}.", items.Count, patch);

        var supportFamily = DetectSupportQuestFamily(items);
        if (supportFamily.RootId > 0)
        {
            logger.LogInformation(
                "Detected support-quest family for patch {Patch}: root={RootId}, intermediates={IntermediateCount}, completions={CompletionCount}.",
                patch, supportFamily.RootId, supportFamily.IntermediateIds.Count, supportFamily.CompletionIds.Count);
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
                    GrantsGrievousWounds = item.Description.Contains("Grievous Wounds", StringComparison.OrdinalIgnoreCase)
                };
            });

        return new PatchItems(metadata, isFallback, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Fetch a patch's item metadata, falling back to <see cref="LatestBranch"/>
    /// when CommunityDragon has not published that patch's branch yet.
    /// </summary>
    /// <remarks>
    /// CommunityDragon mirrors a patch hours-to-days after Riot ships it, so on
    /// every patch day the first games on the new patch reach aggregation while
    /// <c>/&lt;patch&gt;/</c> still 404s. Treating that as fatal aborted both
    /// <c>ChampionPatternAggregation</c> and <c>ChampionPowerspikeAggregation</c>
    /// for the whole live-patch corpus over a handful of new-patch rows (#1107).
    /// The previous patch's item metadata is a far better answer than no run at
    /// all: item ids are stable across patches, and powerspike in particular
    /// flags every match in a batch as folded whether or not it contributed, so
    /// skipping the rows would drop them from the aggregates permanently.
    /// </remarks>
    private async Task<(List<CommunityDragonItem> Items, bool IsFallback)> FetchPatchItemsAsync(
        string patch,
        CancellationToken ct)
    {
        var items = await TryFetchBranchAsync(patch, ct);
        if (items is not null)
        {
            return (items, false);
        }

        logger.LogWarning(
            "CommunityDragon has not published patch {Patch} yet; falling back to its '{Branch}' branch for item metadata.",
            patch,
            LatestBranch);

        var latest = await TryFetchBranchAsync(LatestBranch, ct)
            ?? throw new InvalidOperationException(
                $"CommunityDragon serves neither patch '{patch}' nor its '{LatestBranch}' branch.");

        return (latest, true);
    }

    /// <summary>
    /// Read one CommunityDragon branch, returning <see langword="null"/> when that
    /// branch does not exist. Any other failure still throws — an unpublished
    /// branch is expected, an outage is not.
    /// </summary>
    private async Task<List<CommunityDragonItem>?> TryFetchBranchAsync(string branch, CancellationToken ct)
    {
        var url = $"https://raw.communitydragon.org/{branch}/plugins/rcp-be-lol-game-data/global/default/v1/items.json";
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
    private sealed record PatchItems(
        IReadOnlyDictionary<int, ItemMetadata> Items,
        bool IsFallback,
        DateTimeOffset LoadedAtUtc);

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
