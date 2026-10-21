namespace Data.Statics;

/// <summary>Per-patch item stats (#1750), keyed by item id.</summary>
public interface IItemStatsProvider
{
    /// <summary>
    /// The items of the patch <paramref name="gameVersion"/> belongs to. Throws when the source
    /// is unreachable — a caller that can live without the in-game axes catches and carries on.
    /// </summary>
    Task<IReadOnlyDictionary<int, ItemStats>> GetItemsAsync(string gameVersion, CancellationToken ct);
}
