using Core.Lol.Patches;
using Data;
using Data.BuildFacts;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.ItemRoutes;

public interface IItemRouteQueryService
{
    /// <summary>
    /// The build paths of a champion at a position on <paramref name="patch"/> (#2183), for the
    /// next-item answer and the champion page's item context. Never throws over the item
    /// metadata: without recipes there is no path to lay out, and the answer it rides on is
    /// still worth serving, so the book comes back empty.
    /// </summary>
    Task<ItemRouteBook> GetAsync(int championId, string position, string patch, CancellationToken ct);
}

/// <summary>
/// Reads the component-order counters of one champion at one position over the served patch
/// and the <see cref="MaxPatchWindow"/> − 1 before it: one range of the grain index, cached per
/// aggregation version. The recipes and prices come from the item metadata the API already
/// loads per patch, outside the cache, so a metadata outage is never remembered as a champion
/// with no paths.
/// </summary>
public sealed class ItemRouteQueryService(
    TrueMainDbContext db,
    IItemMetadataProvider itemMetadata,
    IChampionReadCache cache,
    ILogger<ItemRouteQueryService> logger) : IItemRouteQueryService
{
    /// <summary>Patches an item may reach back over to hold the floor, the served one included.</summary>
    public const int MaxPatchWindow = 3;

    public async Task<ItemRouteBook> GetAsync(int championId, string position, string patch, CancellationToken ct)
    {
        if (!PatchVersion.TryParse(patch, out _))
        {
            return ItemRouteBook.Empty;
        }

        IReadOnlyDictionary<int, ItemMetadata> items;
        try
        {
            items = await itemMetadata.GetItemsAsync(patch, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Item metadata unavailable for patch {Patch}; serving no build paths.", patch);
            return ItemRouteBook.Empty;
        }

        var patches = await cache.GetOrComputeAsync(
            $"champions:item-routes:{championId}:{position}:{patch}",
            token => LoadAsync(championId, position, patch, token),
            ct);

        return new ItemRouteBook(patches, items);
    }

    private async Task<IReadOnlyList<IReadOnlyList<ComponentOrderCount>>> LoadAsync(
        int championId,
        string position,
        string patch,
        CancellationToken ct)
    {
        var served = PatchVersion.Parse(patch);
        var rows = await db.ChampionItemComponentOrders
            .AsNoTracking()
            .Where(row => row.ChampionId == championId && row.Position == position)
            .Select(row => new { row.Patch, row.ItemId, row.ComponentId, row.Occurrence, row.Rank, row.Games })
            .ToListAsync(ct);

        // Newest first on parsed versions — '16.9' sorts above '16.19' as a string.
        return
        [
            .. rows
                .GroupBy(row => row.Patch, StringComparer.Ordinal)
                .Select(group => PatchVersion.TryParse(group.Key, out var version) ? (Version: version, Rows: group) : default)
                .Where(entry => entry.Rows is not null && entry.Version.CompareTo(served) <= 0)
                .OrderByDescending(entry => entry.Version)
                .Take(MaxPatchWindow)
                .Select(entry => (IReadOnlyList<ComponentOrderCount>)[.. entry.Rows.Select(row => new ComponentOrderCount(row.ItemId, row.ComponentId, row.Occurrence, row.Rank, row.Games))])
        ];
    }
}
