using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

using TrueMain.Services.Champions.Builds;

namespace TrueMain.Services.Champions.Composition;

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

/// <summary>
/// Folds the top-K participants' raw build data into a win-weighted
/// <see cref="CompositionBuildRecommendation"/>. Fact extraction is the shared
/// <see cref="ParticipantBuildFactsLoader"/>, so this and the matchup-scoped champion
/// page (#923) read a game identically; the folding itself is the pure
/// <see cref="CompositionBuildAggregator"/>.
/// </summary>
public sealed class CompositionBuildQueryService(
    TrueMainDbContext db,
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
        var truncatedKeys = new List<ParticipantKey>();
        var weightByKey = new Dictionary<ParticipantKey, double>(matches.Count);
        foreach (var match in matches)
        {
            var key = new ParticipantKey(match.MatchId, match.ParticipantId);
            if (!weightByKey.ContainsKey(key))
            {
                (match.IsTruncated ? truncatedKeys : keys).Add(key);
                // Similarity, patch recency and pilot, multiplied (#1659) — the
                // aggregator applies the win weight on top.
                weightByKey[key] = CompositionVoteWeight.For(match, maxPossibleScore, options);
            }
        }

        var facts = await factsLoader.LoadAsync(
            keys, championId, position, key => weightByKey[key], ct);
        facts.AddRange(await LoadTruncatedFactsAsync(truncatedKeys, championId, position, weightByKey, ct));

        return CompositionBuildAggregator.Aggregate(facts, options.WinWeight);
    }

    /// <summary>
    /// The facts of the selected games past match retention (#2368), resolved at truncation and
    /// kept in the selection's order, after the live ones.
    /// </summary>
    private async Task<IEnumerable<CompositionParticipantFacts>> LoadTruncatedFactsAsync(
        List<ParticipantKey> keys,
        int championId,
        string position,
        Dictionary<ParticipantKey, double> weightByKey,
        CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var matchIds = keys.Select(key => key.MatchId).Distinct().ToList();
        var seats = await db.TruncatedMatchSeats
            .AsNoTracking()
            .Where(seat => matchIds.Contains(seat.MatchId)
                && seat.ChampionId == championId
                && seat.TeamPosition == position)
            .ToDictionaryAsync(seat => new ParticipantKey(seat.MatchId, seat.ParticipantId), ct);

        return keys
            .Where(seats.ContainsKey)
            .Select(key => TruncatedSeatFacts.From(seats[key], weightByKey[key]));
    }
}
