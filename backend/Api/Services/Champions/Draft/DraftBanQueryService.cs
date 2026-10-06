using Core.Lol.Draft;
using Core.Lol.Ranking;
using Data;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Directory;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Draft;

public interface IDraftBanQueryService
{
    Task<DraftBanResponse> GetAsync(DraftBanCriteria criteria, CancellationToken ct);
}

/// <summary>
/// Which champion to ban, for this player in this draft (#1906).
/// </summary>
/// <remarks>
/// <para>
/// A ban protects something. With a declared pick — the player's hover during
/// the planning phase — the threats are the lane opponents that pick is
/// measurably behind into, weighted by how often each is played on the lane:
/// a champion our pick loses to but nobody plays is not worth the ban. Without a
/// pick, the same over the player's pool, each pool champion weighted by its
/// mastery. Everything comes from <c>champion_matchup_stats</c> and the lane
/// shares <see cref="IDraftLaneReader"/> gives the picks, so the two answers
/// cannot disagree on a matchup.
/// </para>
/// <para>
/// Never suggested: a champion already banned or picked, one an ally has locked
/// or is hovering (banning a teammate's intended pick is the one suggestion
/// worse than none), and the champions we protect. With nothing to protect, or
/// no threat measured, the lane's most banned champions — labelled with their
/// ban rate, not presented as a threat to the player.
/// </para>
/// <para>
/// The enemy team beyond our lane is not weighed yet: it needs the opposing-pair
/// aggregate of #1713.
/// </para>
/// </remarks>
public sealed class DraftBanQueryService(
    TrueMainDbContext db,
    IDraftPatchScopeResolver patchScopes,
    IDraftLaneReader lanes,
    ILanePriorQueryService lanePriors,
    IChampionReadCache cache)
    : IDraftBanQueryService
{
    /// <summary>Pool champions weighed when no pick is declared — the player's mains, not their whole collection.</summary>
    internal const int MaxPoolTargets = 10;

    /// <summary>Most banned champions considered for the fallback before the lane filter.</summary>
    private const int FallbackBannedConsidered = 40;

    /// <summary>
    /// A champion counts as one of the lane's for the fallback when at least this
    /// share of its games are played there.
    /// </summary>
    internal const double FallbackMinLaneShare = 0.25d;

    public async Task<DraftBanResponse> GetAsync(DraftBanCriteria criteria, CancellationToken ct)
    {
        var position = criteria.Position.ToUpperInvariant();
        var scope = await patchScopes.ResolveAsync(criteria.Patch, ct);
        var (target, targets) = Targets(criteria);

        var excluded = new HashSet<int>(criteria.Bans);
        excluded.UnionWith(criteria.EnemyChampions);
        excluded.UnionWith(criteria.AllyChampions);
        excluded.UnionWith(targets.Select(entry => entry.ChampionId));
        excluded.UnionWith(criteria.Pool.Select(entry => entry.ChampionId));
        if (criteria.PlannedPick is { } planned)
        {
            excluded.Add(planned);
        }

        var suggestions = new List<DraftBanCandidateReadModel>();
        if (targets.Count > 0)
        {
            var records = await lanes.ReadRecordsAsync(
                targets.Select(entry => entry.ChampionId).ToList(), position, scope, criteria.EloBracket, ct);
            var laneGames = await lanes.ReadLaneSharesAsync(position, scope, criteria.EloBracket, ct);
            suggestions = DraftBanScorer.Rank(targets, records, laneGames, excluded, DraftScoringWeights.Default);
        }

        if (suggestions.Count == 0)
        {
            target = targets.Count == 0 ? DraftBanTargets.None : target;
            suggestions = await MostBannedAsync(position, scope, criteria.EloBracket, excluded, ct);
        }

        return new DraftBanResponse
        {
            Position = position,
            Patch = scope.Current,
            PreviousPatch = scope.Previous,
            EloBracket = EloBracket.ResolveToken(criteria.EloBracket),
            Target = target,
            TargetChampionIds = targets.Select(entry => entry.ChampionId).ToList(),
            Candidates = suggestions,
        };
    }

    /// <summary>The champions the bans protect: the declared pick, else the pool's top by mastery.</summary>
    internal static (string Target, List<DraftPoolEntry> Targets) Targets(DraftBanCriteria criteria)
    {
        if (criteria.PlannedPick is { } pick)
        {
            return (DraftBanTargets.Pick, [new DraftPoolEntry(pick, 1d)]);
        }

        var pool = criteria.Pool
            .GroupBy(entry => entry.ChampionId)
            .Select(group => group.OrderByDescending(entry => entry.Weight).First())
            .OrderByDescending(entry => entry.Weight)
            .ThenBy(entry => entry.ChampionId)
            .Take(MaxPoolTargets)
            .ToList();
        if (pool.Count == 0)
        {
            return (DraftBanTargets.None, []);
        }

        // No mastery known (every weight zero or negative): the pool counts evenly.
        if (pool.All(entry => entry.Weight <= 0d))
        {
            pool = pool.Select(entry => entry with { Weight = 1d }).ToList();
        }

        return (DraftBanTargets.Pool, pool);
    }

    /// <summary>
    /// The fallback: the most banned champions of the patch among those played on
    /// our lane, by ban rate.
    /// </summary>
    private async Task<List<DraftBanCandidateReadModel>> MostBannedAsync(
        string position,
        DraftPatchScope scope,
        string? eloBracket,
        IReadOnlySet<int> excluded,
        CancellationToken ct)
    {
        if (scope.Window is not { } window)
        {
            return [];
        }

        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var rates = await cache.GetOrComputeAsync(
            $"champions:draft:ban-rates:{scope.Token}:{EloBracket.ResolveToken(eloBracket)}",
            token => ChampionBanRateQueries.LoadAsync(db, window, bands, token),
            ct);

        // Bans exist for the current patch once its first matches are folded; until
        // then the previous patch's are the honest answer.
        var patch = rates.ContainsKey(window[0]) ? window[0] : window.FirstOrDefault(rates.ContainsKey);
        if (patch is null)
        {
            return [];
        }

        var banRates = rates[patch];
        var mostBanned = banRates.BansByChampion
            .Where(entry => !excluded.Contains(entry.Key) && entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Take(FallbackBannedConsidered)
            .Select(entry => entry.Key)
            .ToList();
        if (mostBanned.Count == 0)
        {
            return [];
        }

        var priors = await lanePriors.GetAsync(mostBanned, scope.Current, ct);
        return mostBanned
            .Where(id => priors.TryGetValue(id, out var prior)
                && prior.ByLane.GetValueOrDefault(position) >= FallbackMinLaneShare)
            .Take(DraftBanScorer.MaxSuggestions)
            .Select(id => new DraftBanCandidateReadModel
            {
                ChampionId = id,
                Score = 0d,
                Reasons =
                [
                    new DraftReasonReadModel
                    {
                        Kind = DraftReasonKinds.BanRate,
                        ChampionId = id,
                        Rate = banRates.RateFor(id),
                        Games = (int)Math.Min(int.MaxValue, banRates.Matches),
                    },
                ],
            })
            .ToList();
    }
}
