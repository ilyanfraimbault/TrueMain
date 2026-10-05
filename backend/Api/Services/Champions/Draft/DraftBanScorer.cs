using Core.Lol.Draft;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Draft;

/// <summary>
/// Ranks the enemy champions by how much they threaten the champions we protect
/// (#1906). Pure, like <see cref="DraftCandidateScorer"/>.
/// </summary>
public static class DraftBanScorer
{
    /// <summary>Suggestions returned — a short strip, not a list to read under a timer.</summary>
    internal const int MaxSuggestions = 5;

    /// <summary>Reasons per suggestion: the protected champions it hurts most.</summary>
    internal const int MaxReasons = 2;

    /// <summary>
    /// Threat of every bannable champion E against the targets: for each target
    /// T, how far T is behind into E (shrunk) times E's share of the lane's games,
    /// weighted by T's share of the targets. E is "bad for our pick" and "likely
    /// to be faced", in one product.
    /// </summary>
    /// <param name="targets">The protected champions, with weights summing to anything positive.</param>
    /// <param name="records">Each target's matchup record at our lane.</param>
    /// <param name="laneGames">Every champion's games at our lane — shares are taken over all of them.</param>
    /// <param name="excluded">Champions that may not be suggested.</param>
    /// <param name="weights">The shared draft weights (the shrinkage <c>k</c>).</param>
    public static List<DraftBanCandidateReadModel> Rank(
        IReadOnlyList<DraftPoolEntry> targets,
        IReadOnlyDictionary<int, DraftLaneRecord> records,
        IReadOnlyDictionary<int, double> laneGames,
        IReadOnlySet<int> excluded,
        DraftScoringWeights weights)
    {
        var totalWeight = targets.Sum(target => Math.Max(0d, target.Weight));
        var laneTotal = laneGames.Values.Sum();
        if (totalWeight <= 0d || laneTotal <= 0d)
        {
            return [];
        }

        var suggestions = new List<DraftBanCandidateReadModel>();
        foreach (var (enemy, games) in laneGames)
        {
            if (excluded.Contains(enemy) || games <= 0d)
            {
                continue;
            }

            var share = games / laneTotal;
            var parts = new List<(double Threat, DraftReasonReadModel Reason)>();
            foreach (var target in targets.Where(target => target.Weight > 0d))
            {
                var record = records.GetValueOrDefault(target.ChampionId, DraftLaneRecord.Empty);
                if (!record.Versus.TryGetValue(enemy, out var pairing))
                {
                    continue;
                }

                var threat = target.Weight / totalWeight * DraftScoring.Threat(pairing, share, weights.ShrinkGames);
                if (threat <= 0d)
                {
                    continue;
                }

                parts.Add((threat, new DraftReasonReadModel
                {
                    Kind = DraftReasonKinds.LaneThreat,
                    ChampionId = target.ChampionId,
                    Delta = pairing.Delta,
                    Games = pairing.Games,
                    Share = share,
                }));
            }

            if (parts.Count == 0)
            {
                continue;
            }

            suggestions.Add(new DraftBanCandidateReadModel
            {
                ChampionId = enemy,
                Score = parts.Sum(part => part.Threat),
                Reasons = parts
                    .OrderByDescending(part => part.Threat)
                    .ThenBy(part => part.Reason.ChampionId)
                    .Take(MaxReasons)
                    .Select(part => part.Reason)
                    .ToList(),
            });
        }

        return suggestions
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenBy(suggestion => suggestion.ChampionId)
            .Take(MaxSuggestions)
            .ToList();
    }
}
