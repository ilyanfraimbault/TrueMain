namespace Data.ItemContext;

/// <summary>One situation that moved a candidate, and by how much (a natural-log ratio).</summary>
public readonly record struct NextItemContribution(ItemContextAxis Axis, ItemContextBucket Bucket, double Weight);

/// <summary>A candidate as scored against one game.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Share">Its predicted share of the branch in this game, over the candidates left.</param>
/// <param name="BaseShare">Its share of the branch whatever the game, over the same candidates.</param>
/// <param name="Games">Games of the branch that took it.</param>
/// <param name="Wins">Of which won.</param>
/// <param name="Contributions">The situations that moved it, largest shift first.</param>
public sealed record NextItemScore(
    int ItemId,
    double Share,
    double BaseShare,
    int Games,
    int Wins,
    IReadOnlyList<NextItemContribution> Contributions);

/// <summary>
/// Scores a branch's candidates against the situations one game sits in (#1749).
/// </summary>
/// <remarks>
/// <para>
/// <b>A naive-Bayes combination of measured shifts.</b> Each term is
/// <c>ln P(item | situation) − ln P(item)</c>, measured on the branch's own games, so the
/// score <c>ln P(item) + Σ terms</c> is the log of the item's share once every situation
/// is accounted for, up to a constant the normalisation removes. Nothing here is fitted
/// against outcomes: the prediction is what the cohort <em>chooses</em> in a game like
/// this one, never the item with the best win rate — an item's win rate is mostly a
/// statement about the game state it gets bought in.
/// </para>
/// <para>
/// <b>Correlated situations are summed anyway, on measurement.</b> Naive Bayes assumes the
/// situations independent, and some of ours overlap — a magic-heavy team usually has a
/// magic-damage lane opponent. Letting each group of related axes speak once was tried and
/// measured against plain summing on held-out games (#1749): summing scored as well or
/// slightly better on every slot, so the simpler rule stands.
/// </para>
/// </remarks>
public static class NextItemScorer
{
    public static IReadOnlyList<NextItemScore> Score(
        NextItemBranch branch,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> situation,
        IReadOnlySet<int> excluded)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(situation);
        ArgumentNullException.ThrowIfNull(excluded);

        var open = branch.Candidates.Where(candidate => !excluded.Contains(candidate.ItemId)).ToList();
        if (open.Count == 0)
        {
            return [];
        }

        var scored = open
            .Select(candidate =>
            {
                var contributions = Contributions(candidate, situation);
                return (Candidate: candidate, Contributions: contributions, Log: candidate.BaseLogShare + contributions.Sum(c => c.Weight));
            })
            .ToList();

        var shares = Normalize(scored.Select(entry => entry.Log).ToList());
        var baseShares = Normalize(scored.Select(entry => entry.Candidate.BaseLogShare).ToList());

        return [.. scored
            .Select((entry, index) => new NextItemScore(
                entry.Candidate.ItemId,
                shares[index],
                baseShares[index],
                entry.Candidate.Games,
                entry.Candidate.Wins,
                entry.Contributions))
            .OrderByDescending(score => score.Share)
            .ThenByDescending(score => score.Games)];
    }

    private static List<NextItemContribution> Contributions(
        NextItemCandidate candidate,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> situation)
        => [.. situation
            .Where(entry => entry.Key != ItemContextAxis.Overall)
            .Select(entry => candidate.Lifts.TryGetValue((entry.Key, entry.Value), out var weight)
                ? new NextItemContribution(entry.Key, entry.Value, weight)
                : (NextItemContribution?)null)
            .OfType<NextItemContribution>()
            .OrderByDescending(contribution => Math.Abs(contribution.Weight))];

    /// <summary>Softmax over log-scores, shifted by the maximum so no exponent overflows.</summary>
    private static double[] Normalize(IReadOnlyList<double> logs)
    {
        var max = logs.Max();
        var exps = logs.Select(log => Math.Exp(log - max)).ToArray();
        var sum = exps.Sum();
        return [.. exps.Select(value => value / sum)];
    }
}
