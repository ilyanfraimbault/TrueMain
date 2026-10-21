using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Reads the shop classes of the game's items from CommunityDragon's dump of the game's item
/// data (<c>game/items.cdtb.bin.json</c>), once per patch per process: each <c>ItemData</c>
/// entry's <c>itemID</c> and <c>mItemAttributes</c>, a list of <see cref="ItemShopClass"/> bits.
/// </summary>
/// <remarks>
/// Neither Data Dragon nor the client's <c>items.json</c> carries the classes (their categories
/// are stat tags), so the game's own data is the only source. The dump is large (about 16 MB),
/// which is why it is read here once per patch and served small, never by the desktop app
/// itself. Like the item-metadata provider, a patch CommunityDragon has not published yet is
/// answered from its <c>latest</c> branch — read once for all such patches, the patch probed again
/// once <see cref="FallbackRecheckInterval"/> has passed — and a faulted load is evicted so the
/// next call retries. The cache is bounded (<see cref="MaxCachedPatches"/>). A singleton, so the
/// cache outlives a request; each load takes a fresh client from <paramref name="createClient"/>
/// (the host's <c>IHttpClientFactory</c>), so no client outlives its handler's rotation.
/// </remarks>
public sealed class CommunityDragonItemClassProvider(
    Func<HttpClient> createClient,
    ILogger<CommunityDragonItemClassProvider> logger,
    TimeProvider timeProvider) : IItemClassProvider
{
    private const string LatestBranch = "latest";

    /// <summary>How long a patch read from the fallback branch stands before its own branch is probed again.</summary>
    private static readonly TimeSpan FallbackRecheckInterval = TimeSpan.FromMinutes(30);

    private static readonly ItemShopClass[] KnownClasses = Enum.GetValues<ItemShopClass>()
        .Where(value => value != ItemShopClass.None)
        .ToArray();

    /// <summary>
    /// More patches than a running app ever asks for: past it the cache is emptied but for the latest
    /// branch, so a caller naming patch after patch cannot grow it without bound.
    /// </summary>
    private const int MaxCachedPatches = 8;

    private readonly ConcurrentDictionary<string, Lazy<Task<Loaded>>> _cache = new(StringComparer.Ordinal);

    public async Task<ItemClasses> GetClassesAsync(string? gameVersion, CancellationToken ct)
    {
        var patch = string.IsNullOrWhiteSpace(gameVersion)
            ? LatestBranch
            : PatchVersion.Parse(gameVersion).ToMajorMinor();

        // Two attempts at most: the second runs against an entry this call just created.
        for (var attempt = 0; ; attempt++)
        {
            var lazyTask = Entry(patch);

            Loaded loaded;
            try
            {
                loaded = await lazyTask.Value.WaitAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Evict(patch, lazyTask);
                throw;
            }

            if (loaded.Classes is not null)
            {
                return loaded.Classes;
            }

            // Not published yet: probed again once the wait is over, the latest branch meanwhile —
            // read once for every such patch, never once per patch.
            if (attempt == 0 && timeProvider.GetUtcNow() - loaded.LoadedAtUtc >= FallbackRecheckInterval)
            {
                Evict(patch, lazyTask);
                continue;
            }

            logger.LogDebug("CommunityDragon has not published patch {Patch} yet; serving its '{Branch}' branch.", patch, LatestBranch);
            return await GetClassesAsync(null, ct);
        }
    }

    private Lazy<Task<Loaded>> Entry(string patch)
    {
        if (!_cache.ContainsKey(patch) && _cache.Count >= MaxCachedPatches)
        {
            foreach (var key in _cache.Keys.Where(key => key != LatestBranch))
            {
                _cache.TryRemove(key, out _);
            }
        }

        return _cache.GetOrAdd(patch, static (branch, provider) =>
            new Lazy<Task<Loaded>>(
                () => provider.LoadAsync(branch, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);
    }

    private void Evict(string patch, Lazy<Task<Loaded>> entry)
        => ((ICollection<KeyValuePair<string, Lazy<Task<Loaded>>>>)_cache)
            .Remove(new KeyValuePair<string, Lazy<Task<Loaded>>>(patch, entry));

    /// <summary>A branch read, or <see langword="null"/> classes when it is not published yet, and when.</summary>
    private sealed record Loaded(ItemClasses? Classes, DateTimeOffset LoadedAtUtc);

    private async Task<Loaded> LoadAsync(string branch, CancellationToken ct)
    {
        var items = await TryReadBranchAsync(branch, ct);
        if (items is null && branch == LatestBranch)
        {
            throw new InvalidOperationException($"CommunityDragon does not serve its '{LatestBranch}' branch.");
        }

        if (items is not null)
        {
            logger.LogInformation("Loaded shop classes for {Count} items from CommunityDragon '{Branch}'.", items.Count, branch);
        }

        return new Loaded(items is null ? null : new ItemClasses(branch, items), timeProvider.GetUtcNow());
    }

    /// <summary>
    /// One branch's item classes, or <see langword="null"/> when that branch does not exist.
    /// Any other failure throws — an unpublished branch is expected, an outage is not.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, IReadOnlyList<ItemShopClass>>?> TryReadBranchAsync(string branch, CancellationToken ct)
    {
        var url = $"https://raw.communitydragon.org/{branch}/game/items.cdtb.bin.json";
        using var httpClient = createClient();
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
        foreach (var entry in root.EnumerateObject())
        {
            var value = entry.Value;
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("itemID", out var idElement)
                || !idElement.TryGetInt32(out var itemId)
                || !value.TryGetProperty("mItemAttributes", out var attributes)
                || attributes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var classes = new List<ItemShopClass>();
            foreach (var attribute in attributes.EnumerateArray())
            {
                if (!attribute.TryGetInt32(out var bits))
                {
                    continue;
                }

                foreach (var known in KnownClasses)
                {
                    if ((bits & (int)known) != 0 && !classes.Contains(known))
                    {
                        classes.Add(known);
                    }
                }
            }

            if (classes.Count > 0)
            {
                items[itemId] = classes;
            }
        }

        return items;
    }
}
