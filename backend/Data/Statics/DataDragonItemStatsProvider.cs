using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Reads item stats from Data Dragon's <c>item.json</c>, once per patch per process (#1750).
/// </summary>
/// <remarks>
/// Same shape as <see cref="DataDragonChampionStaticsProvider"/>: the newest published version
/// answers a patch Data Dragon has not mirrored yet (an item's armour rarely moves between two
/// patches, and the alternative is no in-game axis for the first games of the new one), and a
/// faulted load is evicted so the next batch retries instead of replaying the error.
/// </remarks>
public sealed class DataDragonItemStatsProvider(
    HttpClient httpClient,
    ILogger<DataDragonItemStatsProvider> logger) : IItemStatsProvider
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<int, ItemStats>>>> _cache =
        new(StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<int, ItemStats>> GetItemsAsync(string gameVersion, CancellationToken ct)
    {
        var patch = PatchVersion.Parse(gameVersion).ToMajorMinor();

        var lazyTask = _cache.GetOrAdd(patch, static (normalizedPatch, provider) =>
            new Lazy<Task<IReadOnlyDictionary<int, ItemStats>>>(
                () => provider.LoadAsync(normalizedPatch, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);

        try
        {
            return await lazyTask.Value.WaitAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<int, ItemStats>>>>>)_cache)
                .Remove(new KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<int, ItemStats>>>>(patch, lazyTask));
            throw;
        }
    }

    private async Task<IReadOnlyDictionary<int, ItemStats>> LoadAsync(string patch, CancellationToken ct)
    {
        var version = await DataDragonVersions.ResolveAsync(httpClient, patch, logger, "item stats", ct);

        var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/item.json";
        await using var stream = await httpClient.GetStreamAsync(url, ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var items = Parse(document.RootElement);
        logger.LogInformation("Loaded stats for {Count} items from Data Dragon {Version}.", items.Count, version);
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
