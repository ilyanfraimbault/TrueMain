using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Facade of the composition-based build recommender (#563): top-K similarity
/// selection + win-weighted aggregation behind one short-lived cache.
/// </summary>
public interface ICompositionRecommendationQueryService
{
    Task<CompositionBuildResponse> GetAsync(
        CompositionSearchCriteria criteria,
        CancellationToken ct);
}
