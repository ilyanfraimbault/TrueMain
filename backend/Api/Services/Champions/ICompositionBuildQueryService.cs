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
    /// the selected rows inside their matches;
    /// <paramref name="maxPossibleScore"/> normalises each match's similarity
    /// score into its vote weight (0 when no slot was requested — every game
    /// then votes with weight 1).
    /// </summary>
    Task<CompositionBuildRecommendation> AggregateAsync(
        int championId,
        string position,
        IReadOnlyList<CompositionMatchRef> matches,
        int maxPossibleScore,
        CancellationToken ct);
}
