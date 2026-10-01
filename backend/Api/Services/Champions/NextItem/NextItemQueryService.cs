using Core.Lol.Patches;
using Data;
using Data.ItemContext;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.NextItem;

public interface INextItemQueryService
{
    Task<ChampionNextItemResponse> GetAsync(int championId, NextItemCriteria criteria, CancellationToken ct);
}

/// <summary>
/// Answers "what do I complete next" for one live game (#1749), from the next-item model the
/// item-context fold derives (<c>champion_next_item_terms</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A lookup and a sum.</b> The desktop app asks on every item change of any of the ten
/// players, so nothing here reads a match: the champion's model (one range of the terms
/// table) and the patch's champion profiles are each loaded once per aggregation version
/// through <see cref="IChampionReadCache"/>, and a request is the arithmetic of
/// <see cref="NextItemScorer"/> over them.
/// </para>
/// <para>
/// <b>The situation is the fold's, exactly.</b> The game is placed on the axes by the same
/// <see cref="DraftAxisEvaluator"/>, from the same profile snapshot rules, with the same
/// thresholds as the counters were folded with — a bucket computed differently here would
/// read a term measured on other games. The thresholds are the class defaults on both sides;
/// an ingestor override of <c>ItemContextAggregation:Axes</c> must be mirrored here.
/// </para>
/// </remarks>
public sealed class NextItemQueryService(
    TrueMainDbContext db,
    ILanePriorQueryService lanePriors,
    IChampionReadCache cache) : INextItemQueryService
{
    /// <summary>The fold's <c>ProfileLookbackPatches</c>: profiles fill over a patch, so the snapshot reaches back.</summary>
    private const int ProfileLookbackPatches = 2;

    /// <summary>The fold's <c>MinProfileGames</c>: below it a champion is not classified at all.</summary>
    private const int MinProfileGames = 100;

    /// <summary>Reasons returned per candidate — what a panel line can carry.</summary>
    private const int MaxReasons = 3;

    private static readonly DraftAxisThresholds Thresholds = new();

    public async Task<ChampionNextItemResponse> GetAsync(int championId, NextItemCriteria criteria, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var position = criteria.Position;
        var requestedPatch = PatchFilter.Normalize(criteria.Patch);

        var (patch, model) = await cache.GetOrComputeAsync(
            $"champions:next-item:model:{championId}:{position}:{requestedPatch ?? "auto"}",
            token => LoadModelAsync(championId, position, requestedPatch, token),
            ct,
            size: 4);

        if (patch is null || model.IsEmpty)
        {
            return new ChampionNextItemResponse { ChampionId = championId, Position = position, Patch = patch };
        }

        var profiles = await cache.GetOrComputeAsync(
            $"champions:next-item:profiles:{patch}",
            token => ChampionProfileSnapshot.LoadAsync(db, patch, ProfileLookbackPatches, MinProfileGames, token),
            ct,
            size: 8);

        var enemyLanes = await ResolveEnemyLanesAsync(criteria.Enemies, patch, ct);
        var opponent = enemyLanes.FirstOrDefault(lane => lane.Position == position);

        var situation = DraftAxisEvaluator.Evaluate(
            new DraftContext(
                Side(enemyLanes.Select(lane => new NextItemParticipant(lane.ChampionId, lane.Position)), profiles),
                Side(criteria.Allies, profiles),
                opponent is null ? null : profiles.Find(opponent.ChampionId, opponent.Position),
                criteria.GoldLeadAt15),
            Thresholds);

        var owned = criteria.Items.Where(item => item > 0).ToHashSet();

        return new ChampionNextItemResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = patch,
            Build = BuildSlot(model, criteria.Items, owned, situation),
            Boots = owned.Any(model.IsBoots) ? null : Slot(model.Branch(ItemContextSlot.Boots, 0), owned, situation, onTree: true),
            Situation = situation.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value.ToString()),
            EnemyLanes = enemyLanes,
        };
    }

    /// <summary>
    /// The build decision: the branch the completed items lead to, or — when everything that
    /// branch offers is already held — the branches before it, back to the root.
    /// </summary>
    private static NextItemSlotReadModel? BuildSlot(
        NextItemModel model,
        IReadOnlyList<int> items,
        IReadOnlySet<int> owned,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> situation)
    {
        var (path, onTree) = model.ResolveBuildPath(items);

        for (var i = path.Count - 1; i >= 0; i--)
        {
            var slot = Slot(model.Branch(ItemContextSlot.Build, path[i]), owned, situation, onTree && i == path.Count - 1);
            if (slot is not null)
            {
                return slot;
            }
        }

        return null;
    }

    private static NextItemSlotReadModel? Slot(
        NextItemBranch? branch,
        IReadOnlySet<int> owned,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> situation,
        bool onTree)
    {
        if (branch is null)
        {
            return null;
        }

        var scores = NextItemScorer.Score(branch, situation, owned);
        if (scores.Count == 0)
        {
            return null;
        }

        return new NextItemSlotReadModel
        {
            ParentItemId = branch.ParentItemId,
            OnTree = onTree,
            BranchGames = branch.BranchGames,
            PatchWindow = branch.PatchWindow,
            Candidates = [.. scores.Select(score => new NextItemCandidateReadModel
            {
                ItemId = score.ItemId,
                Share = score.Share,
                BaseShare = score.BaseShare,
                Games = score.Games,
                Wins = score.Wins,
                Reasons = [.. score.Contributions
                    .Where(contribution => contribution.Weight > 0)
                    .Take(MaxReasons)
                    .Select(contribution => new NextItemReasonReadModel
                    {
                        Axis = contribution.Axis.ToString(),
                        Bucket = contribution.Bucket.ToString(),
                        Factor = Math.Exp(contribution.Weight),
                    })],
            })],
        };
    }

    /// <summary>
    /// The model of one champion at one position: the requested patch, or the newest one it
    /// has terms for. The newest is chosen on parsed versions — '16.9' sorts above '16.19'
    /// as a string.
    /// </summary>
    private async Task<(string? Patch, NextItemModel Model)> LoadModelAsync(
        int championId,
        string position,
        string? requestedPatch,
        CancellationToken ct)
    {
        var scoped = db.ChampionNextItemTerms
            .AsNoTracking()
            .Where(term => term.ChampionId == championId && term.Position == position);

        var patch = requestedPatch;
        if (patch is null)
        {
            var patches = await scoped.Select(term => term.Patch).Distinct().ToListAsync(ct);
            patch = patches
                .Select(raw => PatchVersion.TryParse(raw, out var version) ? (Raw: raw, Version: version) : default)
                .Where(entry => entry.Raw is not null)
                .OrderByDescending(entry => entry.Version)
                .Select(entry => entry.Raw)
                .FirstOrDefault();
        }

        if (patch is null)
        {
            return (null, NextItemModel.Empty);
        }

        var terms = await scoped.Where(term => term.Patch == patch).ToListAsync(ct);
        return (patch, NextItemModel.From(terms));
    }

    /// <summary>
    /// The enemies on their lanes: the lanes the caller sent stand as given (the app carries
    /// the draft's resolution, corrections included), and the rest are placed around them by
    /// the draft's own solver (#1674), so a game read without lanes still finds its opponent.
    /// </summary>
    private async Task<IReadOnlyList<NextItemEnemyLaneReadModel>> ResolveEnemyLanesAsync(
        IReadOnlyList<NextItemParticipant> enemies,
        string patch,
        CancellationToken ct)
    {
        var given = new Dictionary<int, string>();
        foreach (var enemy in enemies)
        {
            if (enemy.Position is { } lane && !given.ContainsKey(enemy.ChampionId) && !given.ContainsValue(lane))
            {
                given[enemy.ChampionId] = lane;
            }
        }

        var champions = enemies.Select(enemy => enemy.ChampionId).Distinct().ToList();
        if (champions.All(given.ContainsKey))
        {
            return [.. champions.Select(id => new NextItemEnemyLaneReadModel { ChampionId = id, Position = given[id], Given = true })];
        }

        var sorted = champions.Order().ToList();
        var priors = await cache.GetOrComputeAsync(
            $"champions:draft:lane-priors:{string.Join(',', sorted)}:{patch}",
            token => lanePriors.GetAsync(sorted, patch, token),
            ct);

        return [.. LaneAssignmentSolver.Solve(champions, priors, given)
            .OrderByDescending(assignment => given.ContainsKey(assignment.ChampionId))
            .Select(assignment => new NextItemEnemyLaneReadModel
            {
                ChampionId = assignment.ChampionId,
                Position = assignment.Position,
                Given = given.ContainsKey(assignment.ChampionId),
            })];
    }

    private static DraftSide Side(IEnumerable<NextItemParticipant> members, ChampionProfileSnapshot profiles)
    {
        var facts = new List<ChampionProfileFacts>();
        var missing = 0;
        foreach (var member in members)
        {
            if (profiles.Find(member.ChampionId, member.Position ?? string.Empty) is { } resolved)
            {
                facts.Add(resolved);
            }
            else
            {
                missing++;
            }
        }

        return new DraftSide(facts, missing);
    }
}
