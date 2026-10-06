using Core.Lol.Draft;
using Core.Lol.Ranking;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Synergies;

namespace TrueMain.Services.Champions.Draft;

public interface IDraftRecommendationQueryService
{
    Task<DraftRecommendationResponse> GetAsync(DraftCriteria criteria, CancellationToken ct);
}

/// <summary>
/// Answers one champion-select state: where the enemies are, who we face, and
/// which of our champions reads best against it.
///
/// <para>
/// The ranking is deliberately <em>not</em> a trained composition model. The
/// reference apps show a win probability out of a black box; we do not have
/// that model, and a fabricated probability would break the rule that every
/// number on the site comes from an endpoint or a measurement. Instead every
/// component is a measured win-rate difference with its games (#1675, reworked
/// in #1906):
/// </para>
/// <list type="bullet">
///   <item><b>lane</b> — the candidate's win rate into the enemy in our lane,
///     minus its win rate at the lane, from <c>champion_matchup_stats</c>; when
///     the lane guess is split, each enemy weighted by the chance the solver
///     gives it of being there (<see cref="LaneAssignmentSolver.LaneOccupancy"/>);</item>
///   <item><b>blind</b> — for the part of the lane still unknown, the same deltas
///     over the opponents still available, weighted by how often each is played
///     on the lane;</item>
///   <item><b>synergy</b> — the mean observed-minus-expected win rate with the
///     allies on the board, read through the synergy service the champion page
///     uses, hovered allies at reduced weight;</item>
///   <item><b>strength</b> — the candidate's win rate at the lane minus the lane's
///     average;</item>
///   <item><b>enemy team</b> — observed-minus-expected win rate against the enemies
///     off our lane, from <c>champion_opponent_stats</c> (#1713), each weighted by the
///     chance it plays elsewhere than our lane.</item>
/// </list>
///
/// <para>
/// Weighed by <see cref="DraftScoringWeights"/> (lane first), each delta shrunk
/// towards zero by its games, so a thin reading is discounted by exactly how thin
/// it is rather than cut at a floor. What this does not capture yet: the damage
/// mix (#1905, #1907 show it, unscored). The response keeps the components separate, with the reasons
/// that carry each pick, so the client can say why rather than show a total.
/// </para>
/// </summary>
public sealed class DraftRecommendationQueryService(
    ILanePriorQueryService lanePriors,
    IDraftPatchScopeResolver patchScopes,
    IDraftLaneReader lanes,
    IChampionSynergyQueryService synergies,
    IDraftEnemyReader enemyReader,
    IChampionReadCache cache)
    : IDraftRecommendationQueryService
{
    public async Task<DraftRecommendationResponse> GetAsync(
        DraftCriteria criteria,
        CancellationToken ct)
    {
        var position = criteria.Position.ToUpperInvariant();
        var scope = await patchScopes.ResolveAsync(criteria.Patch, ct);
        var bracketToken = EloBracket.ResolveToken(criteria.EloBracket);

        var priors = await ReadPriorsAsync(criteria.EnemyChampions, scope, ct);
        var enemyLanes = ResolveEnemyLanes(criteria, priors);
        var opponent = enemyLanes.FirstOrDefault(lane => lane.Position == position);
        var occupancy = LaneAssignmentSolver.LaneOccupancy(
            criteria.EnemyChampions, priors, position, criteria.PinnedEnemyLanes);

        var candidates = EligibleCandidates(criteria);
        var ranked = candidates.Count == 0
            ? []
            : await RankAsync(candidates, position, occupancy, criteria, scope, ct);

        return new DraftRecommendationResponse
        {
            Position = position,
            Patch = scope.Current,
            PreviousPatch = scope.Previous,
            EloBracket = bracketToken,
            EnemyLanes = enemyLanes,
            LaneOpponentChampionId = opponent?.ChampionId,
            LaneOpponentConfidence = opponent?.Confidence ?? 0d,
            LaneOpponentProbability = Math.Clamp(occupancy.Values.Sum(), 0d, 1d),
            Candidates = ranked,
        };
    }

    /// <summary>
    /// The priors are the only table read of the placement, and the same five
    /// champions are asked about on every pick and every correction of one
    /// draft: cached and coalesced like every other champion read.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, LanePrior>> ReadPriorsAsync(
        IReadOnlyList<int> enemyChampions,
        DraftPatchScope scope,
        CancellationToken ct)
    {
        if (enemyChampions.Count == 0)
        {
            return new Dictionary<int, LanePrior>();
        }

        var enemies = enemyChampions.Distinct().Order().ToList();
        return await cache.GetOrComputeAsync(
            $"champions:draft:lane-priors:{string.Join(',', enemies)}:{scope.Current ?? "all"}",
            token => lanePriors.GetAsync(enemies, scope.Current, token),
            ct);
    }

    /// <summary>Place the enemy champions, honouring the user's pins.</summary>
    private static List<DraftEnemyLaneReadModel> ResolveEnemyLanes(
        DraftCriteria criteria,
        IReadOnlyDictionary<int, LanePrior> priors)
        => LaneAssignmentSolver.Solve(
                criteria.EnemyChampions,
                priors,
                criteria.PinnedEnemyLanes,
                criteria.PreviousEnemyLanes)
            .Select(a => new DraftEnemyLaneReadModel
            {
                ChampionId = a.ChampionId,
                Position = a.Position,
                Confidence = a.Confidence,
                Pinned = a.Pinned,
            })
            .ToList();

    /// <summary>
    /// Every champion already spoken for: banned, picked by the enemy, locked or
    /// hovered by an ally. None of them is ours to pick, and none of them can
    /// still be our lane opponent.
    /// </summary>
    private static HashSet<int> Unavailable(DraftCriteria criteria)
    {
        var unavailable = new HashSet<int>(criteria.Bans);
        unavailable.UnionWith(criteria.EnemyChampions);
        unavailable.UnionWith(criteria.Allies.Values);
        unavailable.UnionWith(criteria.HoveredAllies.Values);
        return unavailable;
    }

    /// <summary>
    /// A candidate we cannot actually pick is not a recommendation, and one an
    /// ally is hovering is theirs: removed before anything is scored.
    /// </summary>
    private static List<int> EligibleCandidates(DraftCriteria criteria)
    {
        var unavailable = Unavailable(criteria);
        return criteria.Candidates.Distinct().Where(id => !unavailable.Contains(id)).ToList();
    }

    private async Task<List<DraftCandidateReadModel>> RankAsync(
        IReadOnlyList<int> candidates,
        string position,
        IReadOnlyDictionary<int, double> occupancy,
        DraftCriteria criteria,
        DraftPatchScope scope,
        CancellationToken ct)
    {
        var records = await lanes.ReadRecordsAsync(candidates, position, scope, criteria.EloBracket, ct);
        var shares = await lanes.ReadLaneSharesAsync(position, scope, criteria.EloBracket, ct);
        var unavailable = Unavailable(criteria);
        var available = shares
            .Where(entry => !unavailable.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        var laneAverage = await lanes.ReadLaneAverageAsync(position, scope, criteria.EloBracket, ct);
        var allies = await ReadAlliesAsync(candidates, position, criteria, scope, ct);
        var enemyPairings = await enemyReader.ReadAsync(
            candidates, position, criteria.EnemyChampions, scope, criteria.EloBracket, ct);
        var weights = DraftScoringWeights.Default;

        return candidates
            .Select(championId => DraftCandidateScorer.Score(
                championId,
                records.GetValueOrDefault(championId, DraftLaneRecord.Empty),
                occupancy,
                // A champion is never its own opponent: the same champion cannot
                // be picked on both sides.
                available.Where(entry => entry.Key != championId).ToDictionary(e => e.Key, e => e.Value),
                laneAverage,
                allies.GetValueOrDefault(championId, []),
                weights,
                EnemiesFor(championId, criteria.EnemyChampions, occupancy, enemyPairings)))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.ChampionId)
            .ToList();
    }

    /// <summary>
    /// The enemies on the board as the enemy-team term weighs them: each by the
    /// chance it plays elsewhere than our lane, unmeasured ones as no effect (#1713).
    /// </summary>
    private static List<DraftEnemyInput> EnemiesFor(
        int championId,
        IReadOnlyList<int> enemyChampions,
        IReadOnlyDictionary<int, double> occupancy,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, DraftComponent>> pairings)
    {
        var measured = pairings.GetValueOrDefault(championId);
        return enemyChampions
            .Distinct()
            .Select(enemy => new DraftEnemyInput(
                enemy,
                1d - Math.Clamp(occupancy.GetValueOrDefault(enemy), 0d, 1d),
                measured?.GetValueOrDefault(enemy) ?? DraftComponent.None))
            .ToList();
    }

    /// <summary>
    /// Each candidate's pairing with every ally on the board, locked or hovered.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IChampionSynergyQueryService"/> rather than off
    /// the table directly, and queried once per ally rather than once per
    /// candidate: asking "who does this ally pair well with, at my lane" returns
    /// every candidate in one call, and the numbers are literally the ones the
    /// champion page shows. A candidate the current patch has no pairing for is
    /// looked up on the previous one. An ally with no measured pairing still
    /// counts in the mean, as no effect.
    /// </remarks>
    private async Task<Dictionary<int, List<DraftAllyInput>>> ReadAlliesAsync(
        IReadOnlyList<int> candidates,
        string position,
        DraftCriteria criteria,
        DraftPatchScope scope,
        CancellationToken ct)
    {
        var result = candidates.ToDictionary(id => id, _ => new List<DraftAllyInput>());
        var board = criteria.Allies.Select(ally => (ally.Key, ally.Value, Hovered: false))
            .Concat(criteria.HoveredAllies
                .Where(hover => !criteria.Allies.ContainsKey(hover.Key))
                .Select(hover => (hover.Key, hover.Value, Hovered: true)));

        foreach (var (allyPositionRaw, allyChampionId, hovered) in board)
        {
            var allyPosition = allyPositionRaw.ToUpperInvariant();
            if (allyPosition == position)
            {
                continue;
            }

            var pairings = await ReadPairingsAsync(allyChampionId, allyPosition, position, scope.Current, criteria, ct);
            if (scope.Previous is not null && candidates.Any(id => !pairings.ContainsKey(id)))
            {
                var previous = await ReadPairingsAsync(
                    allyChampionId, allyPosition, position, scope.Previous, criteria, ct);
                foreach (var (championId, pairing) in previous)
                {
                    pairings.TryAdd(championId, pairing);
                }
            }

            foreach (var championId in candidates)
            {
                result[championId].Add(new DraftAllyInput(
                    allyChampionId,
                    allyPosition,
                    hovered,
                    pairings.GetValueOrDefault(championId, DraftComponent.None)));
            }
        }

        return result;
    }

    private async Task<Dictionary<int, DraftComponent>> ReadPairingsAsync(
        int allyChampionId,
        string allyPosition,
        string position,
        string? patch,
        DraftCriteria criteria,
        CancellationToken ct)
    {
        var response = await synergies.GetSynergiesAsync(
            allyChampionId, allyPosition, patch, position, criteria.EloBracket, ct);
        return response.Partners
            .GroupBy(partner => partner.PartnerChampionId)
            .ToDictionary(group => group.Key, group =>
            {
                var partner = group.First();
                return new DraftComponent(partner.Synergy, partner.Games);
            });
    }
}
