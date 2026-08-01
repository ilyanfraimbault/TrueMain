using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Folds the top-K participants' raw build data into a win-weighted
/// <see cref="CompositionBuildRecommendation"/>. Fact extraction is the shared
/// <see cref="ParticipantBuildFactsLoader"/>, so this and the matchup-scoped champion
/// page (#923) read a game identically; the folding itself is the pure
/// <see cref="CompositionBuildAggregator"/>.
/// </summary>
public sealed class CompositionBuildQueryService(
    ParticipantBuildFactsLoader factsLoader,
    IOptions<CompositionSearchOptions> searchOptions)
    : ICompositionBuildQueryService
{
    public async Task<CompositionBuildRecommendation> AggregateAsync(
        int championId,
        string position,
        IReadOnlyList<CompositionMatchRef> matches,
        int maxPossibleScore,
        CancellationToken ct)
    {
        if (matches.Count == 0)
        {
            return new CompositionBuildRecommendation();
        }

        var options = searchOptions.Value;

        // The incoming top-K is deterministically ranked (score, then game time). That
        // order matters downstream: the aggregator's vote breaks exact weight+games ties
        // on insertion order, so the facts must follow the ranking, not whatever order
        // Postgres returns the rows in.
        var keys = new List<ParticipantKey>(matches.Count);
        var weightByKey = new Dictionary<ParticipantKey, double>(matches.Count);
        foreach (var match in matches)
        {
            var key = new ParticipantKey(match.MatchId, match.ParticipantId);
            if (!weightByKey.ContainsKey(key))
            {
                keys.Add(key);
                // Similarity-proportional vote multiplier: a game reproducing the full
                // draft weighs 1 + boost, an unrelated one weighs 1.
                weightByKey[key] = maxPossibleScore <= 0
                    ? 1d
                    : 1d + options.SimilarityWeightBoost * match.Score / maxPossibleScore;
            }
        }

        var facts = await factsLoader.LoadAsync(
            keys, championId, position, key => weightByKey[key], ct);

        return CompositionBuildAggregator.Aggregate(facts, options.WinWeight);
    }
}
