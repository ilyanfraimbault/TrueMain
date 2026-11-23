using System.Collections.Frozen;
using System.Text.Json;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Reads item stats from Data Dragon's <c>item.json</c> (#1750), through the shared per-patch
/// cache (<see cref="PerPatchStaticCache{T}"/>): an item's armour rarely moves between two patches,
/// so the newest published version answering a patch-day request beats no in-game axis at all.
/// </summary>
public sealed class DataDragonItemStatsProvider : IItemStatsProvider
{
    private readonly Func<HttpClient> _createClient;
    private readonly ILogger<DataDragonItemStatsProvider> _logger;
    private readonly PerPatchStaticCache<IReadOnlyDictionary<int, ItemStats>> _cache;

    public DataDragonItemStatsProvider(
        Func<HttpClient> createClient,
        ILogger<DataDragonItemStatsProvider> logger,
        TimeProvider timeProvider)
    {
        _createClient = createClient;
        _logger = logger;
        _cache = new(
            "Item stats",
            (patch, ct) => LoadAsync(patch, ct),
            async ct => (await LoadAsync(null, ct))!,
            logger,
            timeProvider);
    }

    public Task<StaticData<IReadOnlyDictionary<int, ItemStats>>> GetItemsAsync(string gameVersion, CancellationToken ct)
        => PatchVersion.TryParse(gameVersion, out var patch)
            ? _cache.GetAsync(patch.ToMajorMinor(), ct)
            : Task.FromResult(StaticData<IReadOnlyDictionary<int, ItemStats>>.Unavailable(
                $"'{gameVersion}' is not a patch."));

    private async Task<IReadOnlyDictionary<int, ItemStats>?> LoadAsync(string? patch, CancellationToken ct)
    {
        using var httpClient = _createClient();
        if (await DataDragonVersions.ResolveAsync(httpClient, patch, ct) is not { } version)
        {
            return null;
        }

        var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/item.json";
        await using var stream = await httpClient.GetStreamAsync(url, ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var items = Parse(document.RootElement);
        _logger.LogInformation("Loaded stats for {Count} items from Data Dragon {Version}.", items.Count, version);
        return items;
    }

    /// <summary>The <c>data</c> object of an <c>item.json</c>, item by item.</summary>
    private static Dictionary<int, ItemStats> Parse(JsonElement root)
    {
        var items = new Dictionary<int, ItemStats>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return items;
        }

        foreach (var entry in data.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out var itemId))
            {
                continue;
            }

            var value = entry.Value;
            var stats = value.TryGetProperty("stats", out var raw) && raw.ValueKind == JsonValueKind.Object
                ? raw
                : default;

            items[itemId] = new ItemStats(
                itemId,
                Stat(stats, "FlatArmorMod"),
                Stat(stats, "FlatSpellBlockMod"),
                Stat(stats, "FlatPhysicalDamageMod"),
                Stat(stats, "FlatMagicDamageMod"),
                Stat(stats, "FlatCritChanceMod"),
                Stat(stats, "PercentAttackSpeedMod"),
                Stat(stats, "PercentLifeStealMod"),
                Tags(value));
        }

        return items;
    }

    private static double Stat(JsonElement stats, string name)
        => stats.ValueKind == JsonValueKind.Object
            && stats.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
                ? element.GetDouble()
                : 0d;

    private static FrozenSet<string> Tags(JsonElement item)
        => item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray()
                .Where(tag => tag.ValueKind == JsonValueKind.String)
                .Select(tag => tag.GetString()!)
                .ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;
}
