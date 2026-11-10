using System.Net;
using System.Text.Json;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Reads the shop classes of the game's items from CommunityDragon's dump of the game's item
/// data (<c>game/items.cdtb.bin.json</c>): each <c>ItemData</c> entry's <c>itemID</c> and
/// <c>mItemAttributes</c>, a list of <see cref="ItemShopClass"/> bits, through the shared
/// per-patch cache (<see cref="PerPatchStaticCache{T}"/>).
/// </summary>
/// <remarks>
/// Neither Data Dragon nor the client's <c>items.json</c> carries the classes (their categories
/// are stat tags), so the game's own data is the only source. The dump is large (about 16 MB),
/// which is why it is read here once per patch and served small, never by the desktop app
/// itself. A patch CommunityDragon has not published yet is answered from its <c>latest</c>
/// branch, whose <see cref="ItemClasses.Patch"/> then reads <c>latest</c>.
/// </remarks>
public sealed class CommunityDragonItemClassProvider : IItemClassProvider
{
    private const string LatestBranch = "latest";

    /// <summary>
    /// More patches than a running app ever asks for, so a caller naming patch after patch
    /// cannot grow the cache of a 16 MB source without bound.
    /// </summary>
    private const int MaxCachedPatches = 8;

    private static readonly ItemShopClass[] KnownClasses = Enum.GetValues<ItemShopClass>()
        .Where(value => value != ItemShopClass.None)
        .ToArray();

    private readonly Func<HttpClient> _createClient;
    private readonly ILogger<CommunityDragonItemClassProvider> _logger;
    private readonly PerPatchStaticCache<ItemClasses> _cache;

    public CommunityDragonItemClassProvider(
        Func<HttpClient> createClient,
        ILogger<CommunityDragonItemClassProvider> logger,
        TimeProvider timeProvider)
    {
        _createClient = createClient;
        _logger = logger;
        _cache = new(
            "Item shop classes",
            LoadAsync,
            async ct => await LoadAsync(LatestBranch, ct)
                ?? throw new InvalidOperationException($"CommunityDragon does not serve its '{LatestBranch}' branch."),
            logger,
            timeProvider,
            MaxCachedPatches);
    }

    public Task<StaticData<ItemClasses>> GetClassesAsync(string? gameVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return _cache.GetAsync(null, ct);
        }

        return PatchVersion.TryParse(gameVersion, out var patch)
            ? _cache.GetAsync(patch.ToMajorMinor(), ct)
            : Task.FromResult(StaticData<ItemClasses>.Unavailable($"'{gameVersion}' is not a patch."));
    }

    private async Task<ItemClasses?> LoadAsync(string branch, CancellationToken ct)
    {
        if (await TryReadBranchAsync(branch, ct) is not { } items)
        {
            return null;
        }

        _logger.LogInformation("Loaded shop classes for {Count} items from CommunityDragon '{Branch}'.", items.Count, branch);
        return new ItemClasses(branch, items);
    }

    /// <summary>
    /// One branch's item classes, or <see langword="null"/> when that branch does not exist.
    /// Any other failure throws — an unpublished branch is expected, an outage is not.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, IReadOnlyList<ItemShopClass>>?> TryReadBranchAsync(string branch, CancellationToken ct)
    {
        var url = $"https://raw.communitydragon.org/{branch}/game/items.cdtb.bin.json";
        using var httpClient = _createClient();
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Read(document.RootElement);
    }

    /// <summary>
    /// Every <c>ItemData</c> entry with at least one known class bit, its classes in the data's
    /// order. Entries of other types (item groups, modifiers, spells) are skipped.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<ItemShopClass>> Read(JsonElement root)
    {
        var items = new Dictionary<int, IReadOnlyList<ItemShopClass>>();
        foreach (var value in root.EnumerateObject().Select(entry => entry.Value))
        {
            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("itemID", out var idElement)
                && idElement.TryGetInt32(out var itemId)
                && value.TryGetProperty("mItemAttributes", out var attributes)
                && attributes.ValueKind == JsonValueKind.Array
                && ClassesOf(attributes) is { Count: > 0 } classes)
            {
                items[itemId] = classes;
            }
        }

        return items;
    }

    /// <summary>The classes an item's attribute bits name, in the data's order, each once.</summary>
    private static List<ItemShopClass> ClassesOf(JsonElement attributes)
    {
        var classes = new List<ItemShopClass>();
        foreach (var attribute in attributes.EnumerateArray())
        {
            if (attribute.TryGetInt32(out var bits))
            {
                classes.AddRange(KnownClasses.Where(known => (bits & (int)known) != 0 && !classes.Contains(known)));
            }
        }

        return classes;
    }
}
