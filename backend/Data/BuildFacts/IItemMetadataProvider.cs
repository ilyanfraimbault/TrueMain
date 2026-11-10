using Data.Statics;

namespace Data.BuildFacts;

/// <summary>Per-patch item metadata from the client's <c>items.json</c>, keyed by item id.</summary>
public interface IItemMetadataProvider
{
    /// <summary>
    /// The items of the patch <paramref name="gameVersion"/> belongs to, or why there are none
    /// (<see cref="StaticData{T}"/>). Never throws but on cancellation.
    /// </summary>
    Task<StaticData<IReadOnlyDictionary<int, ItemMetadata>>> GetItemsAsync(string gameVersion, CancellationToken ct);
}
