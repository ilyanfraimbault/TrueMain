using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
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
/// number on the site comes from an endpoint or a measurement. Instead the
/// score is the sum of two measured differences:
/// </para>
/// <list type="bullet">
///   <item>the candidate's win rate against the resolved lane opponent, minus
///     its win rate at that lane overall — both from
///     <c>champion_matchup_stats</c>;</item>
///   <item>its observed-minus-expected win rate with the allies already locked,
///     read through the same synergy service the champion page uses, so the two
///     surfaces can never disagree.</item>
/// </list>
///
/// <para>
/// What this does not capture: global team effects — engage, frontline, damage
/// mix. It sees lanes and pairs. That is less than a trained model, and it is
/// also auditable down to the games that produced it, which a trained model is
/// not. The response keeps both components separate so the client can say which
/// one is carrying a pick rather than showing an unexplained total.
/// </para>
/// </summary>
public sealed class DraftRecommendationQueryService(
    TrueMainDbContext db,
    ILanePriorQueryService lanePriors,
    IChampionSynergyQueryService synergies)
    : IDraftRecommendationQueryService
{
    /// <summary>One half of a candidate's score, with the evidence behind it.</summary>
    private readonly record struct Component(double Delta, int Games)
    {
        public static readonly Component None = new(0d, 0);
    }

    /// <summary>
    /// Below this, a matchup's win rate is noise rather than a reading. The
    /// candidate is still returned and still ranked — dropping a champion the
    /// player owns because we have thin data on it would hide the pick rather
    /// than inform it — but it is flagged.
    /// </summary>
    private const int MinMatchupGames = 30;

    /// <summary>Same idea for the synergy half, summed over the locked allies.</summary>
    private const int MinSynergyGames = 30;

    public async Task<DraftRecommendationResponse> GetAsync(
        DraftCriteria criteria,
        CancellationToken ct)
    {
        var position = criteria.Position.ToUpperInvariant();
        var patch = PatchFilter.Normalize(criteria.Patch);
        var bracketToken = EloBracket.ResolveToken(criteria.EloBracket);

        var enemyLanes = await ResolveEnemyLanesAsync(criteria, patch, ct);
        var opponent = enemyLanes.FirstOrDefault(lane => lane.Position == position);

        var candidates = EligibleCandidates(criteria);
        var ranked = candidates.Count == 0
            ? []
            : await RankAsync(candidates, position, opponent?.ChampionId, criteria, patch, ct);

        return new DraftRecommendationResponse
        {
            Position = position,
            Patch = patch,
            EloBracket = bracketToken,
            EnemyLanes = enemyLanes,
            LaneOpponentChampionId = opponent?.ChampionId,
            LaneOpponentConfidence = opponent?.Confidence ?? 0d,
            Candidates = ranked,
        };
    }

    /// <summary>
    /// Place the enemy champions, honouring the user's pins.
    /// </summary>
    private async Task<List<DraftEnemyLaneReadModel>> ResolveEnemyLanesAsync(
        DraftCriteria criteria,
        string? patch,
        CancellationToken ct)
    {
        if (criteria.EnemyChampions.Count == 0)
        {
            return [];
        }

        var priors = await lanePriors.GetAsync(criteria.EnemyChampions, patch, ct);
        var assignments = LaneAssignmentSolver.Solve(
            criteria.EnemyChampions,
            priors,
            criteria.PinnedEnemyLanes,
            criteria.PreviousEnemyLanes);

        return assignments
            .Select(a => new DraftEnemyLaneReadModel
            {
                ChampionId = a.ChampionId,
                Position = a.Position,
                Confidence = a.Confidence,
                Pinned = a.Pinned,
            })
            .ToList();
    }

    /// <summary>
    /// A candidate we cannot actually pick is not a recommendation. Bans and
    /// champions already on the board are removed before anything is scored, so
    /// the work is not spent on picks the client would have to filter out.
    /// </summary>
    private static List<int> EligibleCandidates(DraftCriteria criteria)
    {
        var unavailable = new HashSet<int>(criteria.Bans);
        unavailable.UnionWith(criteria.EnemyChampions);
        unavailable.UnionWith(criteria.Allies.Values);

        return criteria.Candidates.Distinct().Where(id => !unavailable.Contains(id)).ToList();
    }

    private async Task<List<DraftCandidateReadModel>> RankAsync(
        IReadOnlyList<int> candidates,
        string position,
        int? opponentChampionId,
        DraftCriteria criteria,
        string? patch,
        CancellationToken ct)
    {
        var matchups = await ReadMatchupDeltasAsync(candidates, position, opponentChampionId, patch, ct);
        var synergy = await ReadSynergyDeltasAsync(candidates, position, criteria, patch, ct);

        return candidates
            .Select(championId =>
            {
                var matchup = matchups.GetValueOrDefault(championId, Component.None);
                var pairing = synergy.GetValueOrDefault(championId, Component.None);

                return new DraftCandidateReadModel
                {
                    ChampionId = championId,
                    MatchupDelta = matchup.Delta,
                    MatchupGames = matchup.Games,
                    SynergyDelta = pairing.Delta,
                    SynergyGames = pairing.Games,
                    Score = matchup.Delta + pairing.Delta,
                    ThinSample = matchup.Games < MinMatchupGames
                        && pairing.Games < MinSynergyGames,
                };
            })
            // A proven pick outranks an unproven one at equal score: with no
            // games behind it a candidate scores zero, which would otherwise
            // place it above every champion with a genuinely bad matchup.
            .OrderByDescending(c => c.ThinSample ? 0 : 1)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => c.ChampionId)
            .ToList();
    }

    /// <summary>
    /// Win rate against this opponent, measured against the candidate's own win
    /// rate at the lane.
    /// </summary>
    /// <remarks>
    /// Both halves come from the same table and the same slice, so the
    /// difference is a like-for-like comparison rather than a rate held against
    /// a population it was not measured in. With no opponent resolved yet there
    /// is nothing to compare to, and every candidate gets a zero matchup half —
    /// the synergy half then decides the order on its own.
    /// </remarks>
    private async Task<Dictionary<int, Component>> ReadMatchupDeltasAsync(
        IReadOnlyList<int> candidates,
        string position,
        int? opponentChampionId,
        string? patch,
        CancellationToken ct)
    {
        if (opponentChampionId is null)
        {
            return [];
        }

        var query = db.ChampionMatchupStats
            .AsNoTracking()
            .Where(m => m.TeamPosition == position && candidates.Contains(m.ChampionId));

        if (patch is not null)
        {
            query = query.Where(m => m.Patch.StartsWith(patch));
        }

        var rows = await query
            .GroupBy(m => new { m.ChampionId, m.OpponentChampionId })
            .Select(g => new
            {
                g.Key.ChampionId,
                g.Key.OpponentChampionId,
                Games = g.Sum(m => m.Games),
                Wins = g.Sum(m => m.Wins),
            })
            .ToListAsync(ct);

        var result = new Dictionary<int, Component>();
        foreach (var group in rows.GroupBy(r => r.ChampionId))
        {
            var overallGames = group.Sum(r => r.Games);
            var overallWins = group.Sum(r => r.Wins);
            if (overallGames == 0)
            {
                continue;
            }

            var versus = group.FirstOrDefault(r => r.OpponentChampionId == opponentChampionId);
            if (versus is null || versus.Games == 0)
            {
                // Never seen into this opponent. Zero is "no information", and
                // the thin-sample flag is what tells it apart from "even".
                result[group.Key] = Component.None;
                continue;
            }

            var lanRate = (double)versus.Wins / versus.Games;
            var overallRate = (double)overallWins / overallGames;
            result[group.Key] = new Component(lanRate - overallRate, versus.Games);
        }

        return result;
    }

    /// <summary>
    /// How each candidate fits the allies already locked.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IChampionSynergyQueryService"/> rather than off
    /// the table directly, and queried once per locked ally rather than once per
    /// candidate: asking "who does this ally pair well with, at my lane" returns
    /// every candidate in one call. The numbers are then literally the ones the
    /// champion page shows, so the two surfaces cannot drift apart.
    /// </remarks>
    private async Task<Dictionary<int, Component>> ReadSynergyDeltasAsync(
        IReadOnlyList<int> candidates,
        string position,
        DraftCriteria criteria,
        string? patch,
        CancellationToken ct)
    {
        var totals = new Dictionary<int, Component>();
        var wanted = candidates.ToHashSet();

        foreach (var (allyPosition, allyChampionId) in criteria.Allies)
        {
            if (string.Equals(allyPosition, position, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var response = await synergies.GetSynergiesAsync(
                allyChampionId, allyPosition.ToUpperInvariant(), patch, position, criteria.EloBracket, ct);

            foreach (var partner in response.Partners.Where(p => wanted.Contains(p.PartnerChampionId)))
            {
                var current = totals.GetValueOrDefault(partner.PartnerChampionId, Component.None);
                totals[partner.PartnerChampionId] = new Component(
                    current.Delta + partner.Synergy,
                    current.Games + partner.Games);
            }
        }

        return totals;
    }
}
