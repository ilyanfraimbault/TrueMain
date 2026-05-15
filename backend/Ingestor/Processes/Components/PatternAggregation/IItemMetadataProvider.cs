namespace Ingestor.Processes.Components.PatternAggregation;

public interface IItemMetadataProvider
{
    Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct);
}
