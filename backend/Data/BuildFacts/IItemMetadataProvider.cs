namespace Data.BuildFacts;

public interface IItemMetadataProvider
{
    Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct);
}
