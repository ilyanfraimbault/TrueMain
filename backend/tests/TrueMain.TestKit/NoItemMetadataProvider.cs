using Data.BuildFacts;

namespace TrueMain.TestKit;

/// <summary>
/// An <see cref="IItemMetadataProvider"/> that knows no item, for processes that take one but
/// whose test does not exercise item data (match retention's truncation, #2368).
/// </summary>
public sealed class NoItemMetadataProvider : IItemMetadataProvider
{
    public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<int, ItemMetadata>>(new Dictionary<int, ItemMetadata>());
}
