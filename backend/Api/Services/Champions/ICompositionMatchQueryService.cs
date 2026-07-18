using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Finds the historical games most similar to a requested (possibly partial)
/// draft for a champion at a position — the selection stage of the
/// composition-based build recommender (#563). Hard filter is champion +
/// position only; the composition ranks, it never filters.
/// </summary>
public interface ICompositionMatchQueryService
{
    Task<CompositionMatchesResult> FindTopMatchesAsync(
        CompositionSearchCriteria criteria,
        CancellationToken ct);
}
