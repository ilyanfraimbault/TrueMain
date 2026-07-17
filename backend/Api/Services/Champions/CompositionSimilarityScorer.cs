namespace TrueMain.Services.Champions;

/// <summary>
/// One participant row of a candidate game, seen from the scored candidate:
/// which side it is on relative to the candidate, its position and champion.
/// </summary>
/// <param name="IsEnemy">True when the row is on the opposite team.</param>
/// <param name="TeamPosition">Riot team position of the row.</param>
/// <param name="ChampionId">Champion the row played.</param>
public readonly record struct CompositionSlot(bool IsEnemy, string TeamPosition, int ChampionId);

/// <summary>
/// Similarity weights, snapshotted from <see cref="Options.CompositionSearchOptions"/>
/// so the scorer stays a pure function of its inputs.
/// </summary>
public readonly record struct CompositionScoreWeights(int LaneOpponent, int Enemy, int Ally);

/// <summary>
/// Pure slot-based similarity scoring between a requested (possibly partial)
/// draft and one candidate game. Score = sum of the weights of the requested
/// slots the candidate game reproduces: the lane opponent counts
/// <see cref="CompositionScoreWeights.LaneOpponent"/>, any other enemy slot
/// <see cref="CompositionScoreWeights.Enemy"/>, any ally slot
/// <see cref="CompositionScoreWeights.Ally"/>. Unrequested slots contribute
/// nothing, so partial input just lowers the reachable maximum.
/// </summary>
public static class CompositionSimilarityScorer
{
    /// <summary>
    /// Scores one candidate game against the criteria. <paramref name="slots"/>
    /// are the nine other participants of the candidate game (the candidate row
    /// itself is the hard-filtered champion and is excluded by the caller).
    /// </summary>
    public static int Score(
        CompositionSearchCriteria criteria,
        CompositionScoreWeights weights,
        IEnumerable<CompositionSlot> slots)
    {
        var score = 0;
        foreach (var slot in slots)
        {
            if (slot.IsEnemy)
            {
                if (criteria.Enemies.TryGetValue(slot.TeamPosition, out var enemy)
                    && enemy == slot.ChampionId)
                {
                    score += slot.TeamPosition == criteria.Position
                        ? weights.LaneOpponent
                        : weights.Enemy;
                }
            }
            else if (slot.TeamPosition != criteria.Position
                && criteria.Allies.TryGetValue(slot.TeamPosition, out var ally)
                && ally == slot.ChampionId)
            {
                // The player's own slot is the hard filter, never an ally
                // signal — a stray Allies entry at the player's position is
                // ignored rather than scored.
                score += weights.Ally;
            }
        }

        return score;
    }

    /// <summary>
    /// The score a candidate reproducing every requested slot would reach.
    /// Zero when no slot was requested — the search then degrades to the
    /// most recent champion+position games, which is the intended fallback.
    /// </summary>
    public static int MaxScore(CompositionSearchCriteria criteria, CompositionScoreWeights weights)
    {
        var max = 0;
        foreach (var position in criteria.Enemies.Keys)
        {
            max += position == criteria.Position ? weights.LaneOpponent : weights.Enemy;
        }

        foreach (var position in criteria.Allies.Keys)
        {
            if (position != criteria.Position)
            {
                max += weights.Ally;
            }
        }

        return max;
    }
}
