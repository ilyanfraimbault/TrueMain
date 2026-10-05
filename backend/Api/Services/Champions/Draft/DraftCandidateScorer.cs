using Core.Lol.Draft;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Draft;

/// <summary>An ally on the board and the candidate's measured pairing with it.</summary>
public sealed record DraftAllyInput(int ChampionId, string Position, bool Hovered, DraftComponent Pairing);

/// <summary>
/// An enemy on the board, the candidate's measured pairing against it (#1713), and
/// its weight — the chance it plays elsewhere than our lane.
/// </summary>
public sealed record DraftEnemyInput(int ChampionId, double Weight, DraftComponent Pairing);

/// <summary>
/// Turns one candidate's measured readings into its components, its ranking key
/// and the reasons that carry it (#1906). Pure: the query service reads, this
/// decides — and the tests pin the decisions without a database.
/// </summary>
public static class DraftCandidateScorer
{
    /// <summary>
    /// Below this, a reading is noise rather than a measurement — the display
    /// floor behind <c>thinSample</c>. The ordering does not use it any more: the
    /// shrinkage already discounts a thin delta by exactly how thin it is.
    /// </summary>
    internal const int MinGames = 30;

    /// <summary>Most reasons one card carries — one line on the card, the rest on hover.</summary>
    internal const int MaxReasons = 3;

    /// <summary>
    /// A reason whose part of the score is below this (0.1 pt) carries nothing
    /// and is not said.
    /// </summary>
    internal const double MinContribution = 0.001d;

    /// <summary>The lane opponent must be at least this likely for its lane phase to be stated.</summary>
    internal const double LanePhaseMinProbability = 0.5d;

    public static DraftCandidateReadModel Score(
        int championId,
        DraftLaneRecord record,
        IReadOnlyDictionary<int, double> occupancy,
        IReadOnlyDictionary<int, double> availableShares,
        double laneAverageRate,
        IReadOnlyList<DraftAllyInput> allies,
        DraftScoringWeights weights,
        IReadOnlyList<DraftEnemyInput>? enemies = null)
    {
        enemies ??= [];
        var k = weights.ShrinkGames;
        var strength = DraftScoring.Strength(record.Games, record.Wins, laneAverageRate);
        var (lane, laneShrunk, occupied) = DraftScoring.Lane(record.Versus, occupancy, k);
        var blind = DraftScoring.Blind(record.Versus, availableShares, k);
        var (synergy, synergyShrunk) = DraftScoring.Synergy(
            allies.Select(ally => new DraftAllyPairing(ally.Pairing, ally.Hovered)).ToList(), weights);
        var (enemy, enemyShrunk) = DraftScoring.Enemy(
            enemies.Select(input => new DraftEnemyPairing(input.Pairing, input.Weight)).ToList(), k);
        var score = DraftScoring.Score(
            laneShrunk, occupied, blind.Shrunk, strength.Shrunk(k), synergyShrunk, weights, enemyShrunk);

        var laneGamesInPlay = occupied >= 0.5d ? lane.Games : blind.Games;

        return new DraftCandidateReadModel
        {
            ChampionId = championId,
            MatchupDelta = lane.Delta,
            MatchupGames = lane.Games,
            Blind = new DraftBlindReadModel
            {
                Delta = blind.Delta,
                Games = blind.Games,
                LosingInto = blind.LosingInto,
                LikelyOpponents = blind.LikelyOpponents,
            },
            StrengthDelta = strength.Delta,
            StrengthGames = strength.Games,
            EnemyDelta = enemy.Delta,
            EnemyGames = enemy.Games,
            SynergyDelta = synergy.Delta,
            SynergyGames = synergy.Games,
            Score = score,
            ThinSample = laneGamesInPlay < MinGames && synergy.Games < MinGames,
            Reasons = Reasons(record, occupancy, occupied, blind, strength, allies, enemies, weights),
            Patch = record.Patch,
        };
    }

    /// <summary>
    /// The facts that carry the pick: each score term split back into the parts
    /// it was summed from — one enemy, the blind expectation, one ally — ranked by
    /// how much of the score each part moves, either way. A pick ranked high
    /// despite a bad pairing says so.
    /// </summary>
    private static List<DraftReasonReadModel> Reasons(
        DraftLaneRecord record,
        IReadOnlyDictionary<int, double> occupancy,
        double occupied,
        BlindSafety blind,
        DraftComponent strength,
        IReadOnlyList<DraftAllyInput> allies,
        IReadOnlyList<DraftEnemyInput> enemies,
        DraftScoringWeights weights)
    {
        var k = weights.ShrinkGames;
        var parts = new List<(double Contribution, DraftReasonReadModel Reason)>();

        var occupancyTotal = occupancy.Values.Where(p => p > 0d).Sum();
        foreach (var (enemy, probability) in occupancy)
        {
            if (probability <= 0d || !record.Versus.TryGetValue(enemy, out var pairing) || pairing.Games <= 0)
            {
                continue;
            }

            // The lane term weighs each enemy by its share of the occupied lane,
            // and the occupied lane by its probability: together, the probability.
            var contribution = weights.Lane * occupied * (probability / occupancyTotal) * pairing.Shrunk(k);
            parts.Add((contribution, new DraftReasonReadModel
            {
                Kind = DraftReasonKinds.LaneMatchup,
                ChampionId = enemy,
                Delta = pairing.Delta,
                Games = pairing.Games,
                Probability = probability,
            }));
        }

        if (occupied < 1d && blind.Games > 0)
        {
            parts.Add((weights.Lane * (1d - occupied) * blind.Shrunk, new DraftReasonReadModel
            {
                Kind = DraftReasonKinds.BlindSafety,
                Delta = blind.Delta,
                Games = blind.Games,
                Count = blind.LosingInto,
                Of = blind.LikelyOpponents,
            }));
        }

        if (strength.Games > 0 && weights.Strength > 0d)
        {
            parts.Add((weights.Strength * strength.Shrunk(k), new DraftReasonReadModel
            {
                Kind = DraftReasonKinds.LaneStrength,
                Delta = strength.Delta,
                Games = strength.Games,
            }));
        }

        var enemyWeight = enemies.Sum(input => Math.Max(0d, input.Weight));
        if (weights.Enemy > 0d && enemyWeight > 0d)
        {
            foreach (var input in enemies.Where(input => input.Weight > 0d && input.Pairing.Games > 0))
            {
                parts.Add((weights.Enemy * input.Weight / enemyWeight * input.Pairing.Shrunk(k), new DraftReasonReadModel
                {
                    Kind = DraftReasonKinds.EnemyTeam,
                    ChampionId = input.ChampionId,
                    Delta = input.Pairing.Delta,
                    Games = input.Pairing.Games,
                    Probability = input.Weight,
                }));
            }
        }

        var allyWeight = allies.Sum(ally => ally.Hovered ? weights.HoveredAlly : 1d);
        foreach (var ally in allies.Where(ally => ally.Pairing.Games > 0))
        {
            var weight = ally.Hovered ? weights.HoveredAlly : 1d;
            if (weight <= 0d || allyWeight <= 0d)
            {
                continue;
            }

            parts.Add((weights.Synergy * weight / allyWeight * ally.Pairing.Shrunk(k), new DraftReasonReadModel
            {
                Kind = DraftReasonKinds.Synergy,
                ChampionId = ally.ChampionId,
                Position = ally.Position,
                Delta = ally.Pairing.Delta,
                Games = ally.Pairing.Games,
                Tentative = ally.Hovered,
            }));
        }

        var reasons = parts
            .Where(part => Math.Abs(part.Contribution) >= MinContribution)
            .OrderByDescending(part => Math.Abs(part.Contribution))
            .ThenBy(part => part.Reason.ChampionId ?? 0)
            .Take(MaxReasons)
            .Select(part => part.Reason)
            .ToList();

        // The lane phase is a reason, never a score term: it says how the lane
        // goes, not how the game ends, and the game is what the deltas measure.
        if (reasons.Count < MaxReasons)
        {
            var likeliest = occupancy
                .Where(entry => entry.Value >= LanePhaseMinProbability)
                .OrderByDescending(entry => entry.Value)
                .Select(entry => (int?)entry.Key)
                .FirstOrDefault();
            if (likeliest is { } enemy
                && record.LanePhase.TryGetValue(enemy, out var phase)
                && phase.Wins + phase.Losses >= MinGames)
            {
                reasons.Add(new DraftReasonReadModel
                {
                    Kind = DraftReasonKinds.LanePhase,
                    ChampionId = enemy,
                    Rate = (double)phase.Wins / (phase.Wins + phase.Losses),
                    Games = phase.Wins + phase.Losses,
                });
            }
        }

        return reasons;
    }
}
