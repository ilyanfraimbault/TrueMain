using Core.Lol.Patches;
using Data;
using Data.BuildFacts;
using Data.ItemContext;
using Data.Statics;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;
using TrueMain.Services.Champions.ItemRoutes;
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
/// an ingestor override of <c>ItemContextAggregation:Axes</c> (or <c>:BuiltAxes</c>) must be
/// mirrored here.
/// </para>
/// <para>
/// <b>The in-game axes read the enemies' inventories as the game reports them now</b> (#1750),
/// through the fold's own <see cref="BuiltAxisEvaluator"/> and item catalogue. The fold measured
/// them at the instant the branch opened — the parent's completion — and the panel is asked
/// again on every purchase of anyone, so the live reading starts at that instant and then
/// follows the enemies as they build: what the player sees is the mains' choice against the
/// board in front of them, not against the board of a minute ago.
/// </para>
/// </remarks>
public sealed class NextItemQueryService(
    TrueMainDbContext db,
    ILanePriorQueryService lanePriors,
    IItemRouteQueryService routes,
    IChampionReadCache cache,
    IItemMetadataProvider itemMetadata,
    IItemStatsProvider itemStats,
    ILogger<NextItemQueryService> logger) : INextItemQueryService
{
    /// <summary>A team: the in-game team axes need every enemy's inventory, as the fold does.</summary>
    private const int TeamSize = 5;

    /// <summary>Reasons returned per candidate — what a panel line can carry.</summary>
    private const int MaxReasons = 3;

    private static readonly DraftAxisThresholds Thresholds = new();
    private static readonly BuiltAxisThresholds BuiltThresholds = new();

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
            token => ChampionProfileSnapshotRules.LoadAsync(db, patch, token),
            ct,
            size: 8);

        var enemyLanes = await ResolveEnemyLanesAsync(criteria.Enemies, patch, ct);
        var opponent = enemyLanes.FirstOrDefault(lane => lane.Position == position);

        var draft = DraftAxisEvaluator.Evaluate(
            new DraftContext(
                Side(enemyLanes.Select(lane => new NextItemParticipant(lane.ChampionId, lane.Position)), profiles),
                Side(criteria.Allies, profiles),
                opponent is null ? null : profiles.Find(opponent.ChampionId, opponent.Position),
                criteria.GoldLeadAt15),
            Thresholds);

        var situation = new Dictionary<ItemContextAxis, ItemContextBucket>(draft);
        foreach (var (axis, bucket) in await BuiltSituationAsync(criteria.Enemies, opponent?.ChampionId, patch, ct))
        {
            situation[axis] = bucket;
        }

        var owned = criteria.Items.Where(item => item > 0).ToHashSet();
        var paths = await routes.GetAsync(championId, position, patch, ct);
        var build = WithRoutes(BuildSlot(model, criteria.Items, owned, situation), paths);
        var boots = owned.Any(model.IsBoots) ? null : WithRoutes(Slot(model.Branch(ItemContextSlot.Boots, 0), owned, situation, onTree: true), paths);

        return new ChampionNextItemResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = patch,
            Build = build,
            Boots = boots,
            Order = NextItemBuildOrder.Resolve(model, criteria.Items, situation, build, boots),
            Situation = situation.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value.ToString()),
            EnemyLanes = enemyLanes,
        };
    }

    /// <summary>
    /// Where the enemies' inventories place the game on the in-game axes (#1750): the team axes
    /// when all five sent theirs, the lane-opponent ones when it did. Nothing when no inventory
    /// was sent — champion select — or when the item data cannot be read, in which case the
    /// answer falls back to the draft rather than failing the panel.
    /// </summary>
    private async Task<IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>> BuiltSituationAsync(
        IReadOnlyList<NextItemParticipant> enemies,
        int? opponentChampionId,
        string patch,
        CancellationToken ct)
    {
        var team = enemies.Count == TeamSize && enemies.All(enemy => enemy.Items is not null)
            ? enemies.Select(enemy => enemy.Items!).ToList()
            : null;
        var opponent = enemies.FirstOrDefault(enemy => enemy.ChampionId == opponentChampionId)?.Items;
        if (team is null && opponent is null)
        {
            return new Dictionary<ItemContextAxis, ItemContextBucket>();
        }

        BuiltItemCatalog catalog;
        try
        {
            catalog = await cache.GetOrComputeAsync(
                $"champions:next-item:built-items:{patch}",
                async token => BuiltItemCatalog.From(
                    await itemMetadata.GetItemsAsync(patch, token),
                    await itemStats.GetItemsAsync(patch, token)),
                ct,
                size: 2);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Item data unavailable for patch {Patch}; serving the next item without in-game axes.", patch);
            return new Dictionary<ItemContextAxis, ItemContextBucket>();
        }

        return BuiltAxisEvaluator.Evaluate(new BuiltContext(team, opponent), catalog, BuiltThresholds);
    }

    /// <summary>Each candidate with its build path (#2183), so the app needs no second call.</summary>
    private static NextItemSlotReadModel? WithRoutes(NextItemSlotReadModel? slot, ItemRouteBook paths)
        => slot is null
            ? null
            : slot with
            {
                Candidates = [.. slot.Candidates.Select(candidate => candidate with
                {
                    Route = paths.Route(candidate.ItemId),
                    RouteGames = paths.Games(candidate.ItemId),
                })],
            };

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
                // A reason is a sentence, and only an axis's two ends have one: the middle
                // bucket still moves the share, it just cannot be said.
                Reasons = [.. score.Contributions
                    .Where(contribution => contribution.Weight > 0 && contribution.Bucket != ItemContextBucket.Mid)
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
