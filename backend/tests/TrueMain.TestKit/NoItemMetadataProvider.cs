using Data.BuildFacts;
using Data.Statics;

namespace TrueMain.TestKit;

/// <summary>
/// An <see cref="IItemMetadataProvider"/> that knows no item, for processes that take one but
/// whose test does not exercise item data (match retention's truncation, #2368).
/// </summary>
public sealed class NoItemMetadataProvider : IItemMetadataProvider
{
    public Task<StaticData<IReadOnlyDictionary<int, ItemMetadata>>> GetItemsAsync(string gameVersion, CancellationToken ct)
        => Task.FromResult(StaticData<IReadOnlyDictionary<int, ItemMetadata>>.Available(new Dictionary<int, ItemMetadata>()));
}
