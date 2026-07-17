using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Aggregates a win-weighted build recommendation from the top-K games
/// selected by <see cref="ICompositionMatchQueryService"/> — the second stage
/// of the composition-based build recommender (#563).
/// </summary>
public interface ICompositionBuildQueryService
{
    /// <summary>
    /// Loads the selected participants' raw build data (items, timeline
    /// events, spells, rune selections) and folds it into one recommendation.
    /// <paramref name="championId"/> / <paramref name="position"/> re-identify
    /// the selected rows inside their matches.
    /// </summary>
    Task<CompositionBuildRecommendation> AggregateAsync(
        int championId,
        string position,
        IReadOnlyList<CompositionMatchRef> matches,
        CancellationToken ct);
}
