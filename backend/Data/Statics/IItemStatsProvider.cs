namespace Data.Statics;

/// <summary>Per-patch item stats (#1750), keyed by item id.</summary>
public interface IItemStatsProvider
{
    /// <summary>
    /// The items of the patch <paramref name="gameVersion"/> belongs to, or why there are none
    /// (<see cref="StaticData{T}"/>). Never throws but on cancellation.
    /// </summary>
    Task<StaticData<IReadOnlyDictionary<int, ItemStats>>> GetItemsAsync(string gameVersion, CancellationToken ct);
}
