namespace Core.Lol.Draft;

/// <summary>
/// One measured difference behind a draft suggestion: a win-rate delta in
/// probability points (0.031 = +3.1 pts) and the games it rests on.
/// </summary>
public readonly record struct DraftComponent(double Delta, int Games)
{
    public static readonly DraftComponent None = new(0d, 0);

    /// <summary>
    /// The delta pulled towards zero by how little evidence it has —
    /// <c>delta × n / (n + k)</c>, the empirical-Bayes shrinkage of a rate
    /// difference whose prior is "no effect". A 2,000-game +3 pts stays near
    /// +3; a 20-game +15 pts lands near +2. It orders the suggestions; the
    /// figure shown to the player stays the measured one.
    /// </summary>
    public double Shrunk(double shrinkGames)
        => Games <= 0 ? 0d : Delta * Games / (Games + Math.Max(0d, shrinkGames));
}

/// <summary>
/// How the draft components are weighed against each other (#1906). The order
/// is a product decision — lane first, the team after — and the values are the
/// ones the back-test in <c>backend/Tools/DraftEvaluation</c> settles; the
/// defaults here are what it was run against.
/// </summary>
public sealed record DraftScoringWeights
{
    public static readonly DraftScoringWeights Default = new();

    /// <summary>Weight of the lane term (resolved opponent, or the blind expectation).</summary>
    public double Lane { get; init; } = 1d;

    /// <summary>
    /// Weight of the champion's own strength at the lane — its win rate there minus
    /// the lane's average. The matchup and blind terms are deltas against the
    /// champion's own rate, so without this a champion that wins everywhere scores
    /// like one that loses everywhere.
    /// </summary>
    public double Strength { get; init; } = 0d;

    /// <summary>Weight of the ally synergy term, an average over the allies on the board.</summary>
    public double Synergy { get; init; } = 0.25d;

    /// <summary>
    /// The <c>k</c> of <see cref="DraftComponent.Shrunk"/>: the games at which a
    /// delta keeps half its value.
    /// </summary>
    public double ShrinkGames { get; init; } = 400d;

    /// <summary>
    /// Weight of an ally who is only hovering a champion, against 1 for a locked
    /// one: a hover is an intention the ally can still change.
    /// </summary>
    public double HoveredAlly { get; init; } = 0.5d;
}

/// <summary>An ally on the board, as the synergy term sees it.</summary>
public readonly record struct DraftAllyPairing(DraftComponent Pairing, bool Hovered);

/// <summary>
/// The candidate's expected matchup over the lane opponents still available,
/// before the enemy has shown one (#1906).
/// </summary>
/// <param name="Delta">
/// Measured expectation: the matchup deltas into the opponents we have games
/// on, weighted by how often each opponent is played on the lane.
/// </param>
/// <param name="Games">Games behind those matchups, summed.</param>
/// <param name="Shrunk">
/// The same expectation over every available opponent, each delta shrunk by
/// its games — an unseen opponent counts as no effect. This is what ranks.
/// </param>
/// <param name="LosingInto">
/// Of the <paramref name="LikelyOpponents"/>, how many the candidate is
/// clearly behind into (shrunk delta at or below the threshold).
/// </param>
/// <param name="LikelyOpponents">The most-played available opponents the lower tail is counted over.</param>
public readonly record struct BlindSafety(
    double Delta,
    int Games,
    double Shrunk,
    int LosingInto,
    int LikelyOpponents)
{
    public static readonly BlindSafety None = new(0d, 0, 0d, 0, 0);

    public DraftComponent Component => new(Delta, Games);
}

/// <summary>
/// The arithmetic of the draft suggestions, kept free of any data access so the
/// API and the offline back-test score with the very same code.
/// </summary>
/// <remarks>
/// Every input is a measured win-rate difference with its games; nothing here is
/// a win probability, and the total is a ranking key only — never shown, never
/// named as a chance to win (decision "Draft candidates are ranked by measured
/// deltas", <c>decisions/product-matchups.md</c>).
/// </remarks>
public static class DraftScoring
{
    /// <summary>How many of the lane's most-played opponents the lower tail is counted over.</summary>
    public const int LikelyOpponentCount = 8;

    /// <summary>
    /// A matchup is "clearly behind" at −2 pts or worse once shrunk: a raw −2
    /// over a handful of games does not qualify, a −2 over thousands does.
    /// </summary>
    public const double ClearlyLosingDelta = -0.02d;

    /// <summary>
    /// The candidate's blind-pick safety over <paramref name="opponentShares"/>,
    /// the opponents still available (not banned, not picked) with their weight on
    /// the lane — any non-negative scale, normalised here.
    /// </summary>
    /// <param name="versus">The candidate's matchup delta into each opponent it has games on.</param>
    /// <param name="opponentShares">The available opponents and how much each is played on the lane.</param>
    /// <param name="shrinkGames">The shrinkage <c>k</c>.</param>
    public static BlindSafety Blind(
        IReadOnlyDictionary<int, DraftComponent> versus,
        IReadOnlyDictionary<int, double> opponentShares,
        double shrinkGames)
    {
        var total = opponentShares.Values.Where(share => share > 0d).Sum();
        if (total <= 0d)
        {
            return BlindSafety.None;
        }

        double measuredWeight = 0d, measured = 0d, shrunk = 0d;
        var games = 0;
        foreach (var (opponent, rawShare) in opponentShares)
        {
            if (rawShare <= 0d || !versus.TryGetValue(opponent, out var pairing) || pairing.Games <= 0)
            {
                continue;
            }

            var share = rawShare / total;
            measuredWeight += share;
            measured += share * pairing.Delta;
            shrunk += share * pairing.Shrunk(shrinkGames);
            games += pairing.Games;
        }

        var likely = opponentShares
            .Where(entry => entry.Value > 0d)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Take(LikelyOpponentCount)
            .Select(entry => entry.Key)
            .ToList();
        var losingInto = likely.Count(opponent =>
            versus.TryGetValue(opponent, out var pairing)
            && pairing.Shrunk(shrinkGames) <= ClearlyLosingDelta);

        return new BlindSafety(
            measuredWeight > 0d ? measured / measuredWeight : 0d,
            games,
            shrunk,
            losingInto,
            likely.Count);
    }

    /// <summary>
    /// The candidate's matchup against an enemy whose lane is still a guess:
    /// each enemy's delta weighted by the chance the solver gives it of being in
    /// our lane, conditional on our lane being taken at all.
    /// </summary>
    /// <param name="versus">The candidate's matchup delta into each opponent it has games on.</param>
    /// <param name="occupancy">Per enemy, the probability it plays our lane.</param>
    /// <param name="shrinkGames">The shrinkage <c>k</c>.</param>
    /// <returns>
    /// The measured weighted delta and games, the shrunk weighted delta, and the
    /// total probability our lane opponent is already on the board.
    /// </returns>
    public static (DraftComponent Measured, double Shrunk, double Occupied) Lane(
        IReadOnlyDictionary<int, DraftComponent> versus,
        IReadOnlyDictionary<int, double> occupancy,
        double shrinkGames)
    {
        var occupied = Math.Clamp(occupancy.Values.Where(p => p > 0d).Sum(), 0d, 1d);
        if (occupied <= 0d)
        {
            return (DraftComponent.None, 0d, 0d);
        }

        double weight = 0d, measured = 0d, shrunk = 0d, games = 0d;
        foreach (var (enemy, probability) in occupancy)
        {
            if (probability <= 0d)
            {
                continue;
            }

            var pairing = versus.GetValueOrDefault(enemy, DraftComponent.None);
            shrunk += probability * pairing.Shrunk(shrinkGames);
            if (pairing.Games > 0)
            {
                weight += probability;
                measured += probability * pairing.Delta;
                games += probability * pairing.Games;
            }
        }

        var sum = occupancy.Values.Where(p => p > 0d).Sum();
        return (
            weight > 0d ? new DraftComponent(measured / weight, (int)Math.Round(games / weight)) : DraftComponent.None,
            shrunk / sum,
            occupied);
    }

    /// <summary>
    /// The ally term: the mean shrunk pairing over every ally on the board — an
    /// unmeasured pairing counts as no effect — so four allies weigh what one
    /// does instead of four times as much. Hovered allies count
    /// <see cref="DraftScoringWeights.HoveredAlly"/>.
    /// </summary>
    public static (DraftComponent Measured, double Shrunk) Synergy(
        IReadOnlyList<DraftAllyPairing> allies,
        DraftScoringWeights weights)
    {
        double total = 0d, shrunk = 0d, measuredWeight = 0d, measured = 0d;
        var games = 0;
        foreach (var ally in allies)
        {
            var weight = ally.Hovered ? weights.HoveredAlly : 1d;
            if (weight <= 0d)
            {
                continue;
            }

            total += weight;
            shrunk += weight * ally.Pairing.Shrunk(weights.ShrinkGames);
            if (ally.Pairing.Games > 0)
            {
                measuredWeight += weight;
                measured += weight * ally.Pairing.Delta;
                games += ally.Pairing.Games;
            }
        }

        return (
            measuredWeight > 0d ? new DraftComponent(measured / measuredWeight, games) : DraftComponent.None,
            total > 0d ? shrunk / total : 0d);
    }

    /// <summary>
    /// The ranking key: the lane term — the resolved opponent where the board
    /// shows one, the blind expectation for the share of the lane still unknown —
    /// then the champion's own strength at the lane, then the ally term.
    /// </summary>
    public static double Score(
        double laneShrunk,
        double laneOccupied,
        double blindShrunk,
        double strengthShrunk,
        double synergyShrunk,
        DraftScoringWeights weights)
    {
        var occupied = Math.Clamp(laneOccupied, 0d, 1d);
        var lane = (occupied * laneShrunk) + ((1d - occupied) * blindShrunk);
        return (weights.Lane * lane) + (weights.Strength * strengthShrunk) + (weights.Synergy * synergyShrunk);
    }

    /// <summary>
    /// The champion's strength at the lane: its win rate there minus the lane's
    /// average win rate, over its games at the lane.
    /// </summary>
    public static DraftComponent Strength(int games, int wins, double laneAverageRate)
        => games <= 0 ? DraftComponent.None : new DraftComponent(((double)wins / games) - laneAverageRate, games);

    /// <summary>
    /// How much the enemy champion threatens our pick: how far behind the pick is
    /// into it, shrunk, times how often it is played on our lane. Zero when the
    /// pick is not behind.
    /// </summary>
    public static double Threat(DraftComponent pickVersusEnemy, double enemyLaneShare, double shrinkGames)
        => Math.Max(0d, -pickVersusEnemy.Shrunk(shrinkGames)) * Math.Max(0d, enemyLaneShare);
}
