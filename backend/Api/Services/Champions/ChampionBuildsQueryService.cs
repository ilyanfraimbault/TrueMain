using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionBuildsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options)
    : IChampionBuildsQueryService
{
    private const int MaxBuilds = 4;
    private const double MinBuildPickRate = 0.05;
    private const int VariationsTopN = 3;
    private const int RunePagesTopN = 3;
    private const double ItemPathProbThreshold = 0.20;
    private const int ItemPathMaxDepth = 6;
    private const int BuildTreeMaxDepth = 6;
    private const int BuildTreeMaxChildrenPerNode = 6;
    private const int BuildTreeMinGames = 2;

    public async Task<ChampionResponse?> GetAsync(
        int championId,
        string? patch,
        string? position,
        CancellationToken ct)
    {
        var scopes = await ChampionScopeLoader.LoadAsync(
            db, options.Value.QueueId, championId, patch, position, ct);
        if (scopes is null)
        {
            return null;
        }

        var scopeIds = scopes.Select(scope => scope.Id).ToList();
        var rows = await FetchRowsAsync(scopeIds, ct);
        var totalGames = rows.Sum(row => row.Games);
        var totalWins = rows.Sum(row => row.Wins);

        var resolvedPatch = scopes.First().GameVersion;
        var resolvedPosition = scopes.First().Position;

        if (totalGames == 0 || rows.Count == 0)
        {
            return new ChampionResponse
            {
                ChampionId = championId,
                Patch = resolvedPatch,
                Position = resolvedPosition,
                TotalGames = totalGames,
                TotalWins = totalWins,
                Builds = []
            };
        }

        var groups = rows
            .GroupBy(row => new BuildKey(row.BuildItem0, row.PrimaryKeystoneId))
            .Select(group => new PendingBuild(
                group.Key,
                group.ToList(),
                group.Sum(row => row.Games),
                group.Sum(row => row.Wins)))
            .Where(pending => (double)pending.Games / totalGames > MinBuildPickRate)
            .OrderByDescending(pending => pending.Games)
            .ThenBy(pending => pending.Key.FirstItemId)
            .ThenBy(pending => pending.Key.PrimaryKeystoneId)
            .Take(MaxBuilds)
            .ToList();

        if (groups.Count == 0)
        {
            return new ChampionResponse
            {
                ChampionId = championId,
                Patch = resolvedPatch,
                Position = resolvedPosition,
                TotalGames = totalGames,
                TotalWins = totalWins,
                Builds = []
            };
        }

        var perGroupAggregates = groups
            .Select(pending => AggregateGroup(pending))
            .ToList();

        var spellIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopSpells.Select(t => t.Id)));
        var skillIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopSkills.Select(t => t.Id)));
        var starterIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopStarters.Select(t => t.Id)));
        var runeIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopRunes.Select(t => t.Id)));

        var dimSpellPairs = await db.ChampionDimSpellPairs.AsNoTracking()
            .Where(dim => spellIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimSkillOrders = await db.ChampionDimSkillOrders.AsNoTracking()
            .Where(dim => skillIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimStarterItems = await db.ChampionDimStarterItems.AsNoTracking()
            .Where(dim => starterIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimRunePages = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);

        var builds = perGroupAggregates
            .Select(ga => MaterializeBuild(
                ga, totalGames,
                dimSpellPairs, dimSkillOrders, dimStarterItems, dimRunePages))
            .ToList();

        return new ChampionResponse
        {
            ChampionId = championId,
            Patch = resolvedPatch,
            Position = resolvedPosition,
            TotalGames = totalGames,
            TotalWins = totalWins,
            Builds = builds
        };
    }

    private async Task<IReadOnlyList<ChampionPatternEnrichedRow>> FetchRowsAsync(
        IReadOnlyList<Guid> scopeIds,
        CancellationToken ct)
    {
        // EF cannot translate construction of a positional record inside a
        // join projection, so we project into an anonymous type first (which
        // EF translates to a flat SELECT) and convert to the record after
        // materialisation.
        var raw = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Where(pattern => scopeIds.Contains(pattern.ScopeId))
            .Join(
                db.ChampionDimBuilds.AsNoTracking(),
                pattern => pattern.BuildId,
                build => build.Id,
                (pattern, build) => new { Pattern = pattern, Build = build })
            .Join(
                db.ChampionDimRunePages.AsNoTracking(),
                joined => joined.Pattern.RunePageId,
                rune => rune.Id,
                (joined, rune) => new
                {
                    joined.Pattern.SpellPairId,
                    joined.Pattern.SkillOrderId,
                    joined.Pattern.StarterItemsId,
                    joined.Pattern.RunePageId,
                    joined.Build.BuildItem0,
                    joined.Build.BuildItem1,
                    joined.Build.BuildItem2,
                    joined.Build.BuildItem3,
                    joined.Build.BuildItem4,
                    joined.Build.BuildItem5,
                    joined.Build.BuildItem6,
                    joined.Build.BootsItemId,
                    rune.PrimaryKeystoneId,
                    joined.Pattern.Games,
                    joined.Pattern.Wins
                })
            .Where(row => row.BuildItem0 > 0 && row.PrimaryKeystoneId > 0)
            .ToListAsync(ct);

        return raw
            .Select(row => new ChampionPatternEnrichedRow(
                row.SpellPairId,
                row.SkillOrderId,
                row.StarterItemsId,
                row.RunePageId,
                row.BuildItem0,
                row.BuildItem1,
                row.BuildItem2,
                row.BuildItem3,
                row.BuildItem4,
                row.BuildItem5,
                row.BuildItem6,
                row.BootsItemId,
                row.PrimaryKeystoneId,
                row.Games,
                row.Wins))
            .ToList();
    }

    private static GroupAggregates AggregateGroup(PendingBuild pending)
    {
        var sliceGames = pending.Games;
        var rows = pending.Rows;

        var topSpells = AggregateByGuid(
            rows, r => r.SpellPairId, r => r.Games, r => r.Wins, VariationsTopN);
        var topSkills = AggregateByGuid(
            rows, r => r.SkillOrderId, r => r.Games, r => r.Wins, VariationsTopN);
        var topStarters = AggregateByGuid(
            rows, r => r.StarterItemsId, r => r.Games, r => r.Wins, VariationsTopN);
        var topRunes = AggregateByGuid(
            rows, r => r.RunePageId, r => r.Games, r => r.Wins, RunePagesTopN);
        var topBoots = AggregateBoots(rows, VariationsTopN);

        var (itemPath, itemPathGames, itemPathWins) = ComputeItemPath(
            rows, pending.Key.FirstItemId, sliceGames);
        var buildTree = BuildItemTree(rows);

        return new GroupAggregates(
            pending.Key,
            pending.Rows,
            pending.Games,
            pending.Wins,
            topSpells,
            topSkills,
            topStarters,
            topRunes,
            topBoots,
            itemPath,
            itemPathGames,
            itemPathWins,
            buildTree);
    }

    private static List<DimAggregate> AggregateByGuid(
        IReadOnlyList<ChampionPatternEnrichedRow> rows,
        Func<ChampionPatternEnrichedRow, Guid> idSelector,
        Func<ChampionPatternEnrichedRow, int> gamesSelector,
        Func<ChampionPatternEnrichedRow, int> winsSelector,
        int topN)
        => rows
            .GroupBy(idSelector)
            .Select(group => new DimAggregate(
                group.Key,
                group.Sum(gamesSelector),
                group.Sum(winsSelector)))
            .OrderByDescending(aggregate => aggregate.Games)
            .ThenByDescending(aggregate => aggregate.Wins)
            .ThenBy(aggregate => aggregate.Id)
            .Take(topN)
            .ToList();

    private static List<BootsAggregate> AggregateBoots(
        IReadOnlyList<ChampionPatternEnrichedRow> rows,
        int topN)
        => rows
            .Where(row => row.BootsItemId > 0)
            .GroupBy(row => row.BootsItemId)
            .Select(group => new BootsAggregate(
                group.Key,
                group.Sum(row => row.Games),
                group.Sum(row => row.Wins)))
            .OrderByDescending(aggregate => aggregate.Games)
            .ThenByDescending(aggregate => aggregate.Wins)
            .ThenBy(aggregate => aggregate.ItemId)
            .Take(topN)
            .ToList();

    private static (List<int> ItemIds, int Games, int Wins) ComputeItemPath(
        IReadOnlyList<ChampionPatternEnrichedRow> rows,
        int firstItemId,
        int sliceGames)
    {
        var root = new MutableItemNode(firstItemId) { Games = sliceGames, Wins = rows.Sum(row => row.Wins) };
        foreach (var row in rows)
        {
            var chain = new[]
            {
                row.BuildItem1, row.BuildItem2, row.BuildItem3,
                row.BuildItem4, row.BuildItem5, row.BuildItem6
            };
            var cursor = root;
            foreach (var itemId in chain)
            {
                if (itemId <= 0)
                {
                    break;
                }
                if (!cursor.Children.TryGetValue(itemId, out var child))
                {
                    child = new MutableItemNode(itemId);
                    cursor.Children[itemId] = child;
                }
                child.Games += row.Games;
                child.Wins += row.Wins;
                cursor = child;
            }
        }

        var path = new List<int> { firstItemId };
        var current = root;
        var deepestGames = sliceGames;
        var deepestWins = root.Wins;

        while (path.Count <= ItemPathMaxDepth && current.Children.Count > 0)
        {
            // Pick the child whose subtree extends deepest — keeps the main
            // build "very complete" instead of stopping at the most popular
            // terminal item. Tie-break by games then wins then itemId so the
            // chain stays deterministic.
            var best = current.Children.Values
                .OrderByDescending(MaxDepth)
                .ThenByDescending(node => node.Games)
                .ThenByDescending(node => node.Wins)
                .ThenBy(node => node.ItemId)
                .First();
            // Probability is parent-relative — share of games that *reached*
            // the current node and then went on to pick `best`. Matches the
            // pickrate semantic shown in the build-tree tooltip (see
            // ConvertTreeNode below), so a node displayed as "40% pick" is
            // also the same 40% the threshold sees.
            var probability = current.Games == 0 ? 0d : (double)best.Games / current.Games;
            if (probability < ItemPathProbThreshold)
            {
                break;
            }
            path.Add(best.ItemId);
            deepestGames = best.Games;
            deepestWins = best.Wins;
            current = best;
        }

        return (path, deepestGames, deepestWins);
    }

    private static IReadOnlyList<MutableItemNode> BuildItemTree(
        IReadOnlyList<ChampionPatternEnrichedRow> rows)
    {
        var rootChildren = new Dictionary<int, MutableItemNode>();
        foreach (var row in rows)
        {
            var chain = new[]
            {
                row.BuildItem1, row.BuildItem2, row.BuildItem3,
                row.BuildItem4, row.BuildItem5, row.BuildItem6
            };
            Dictionary<int, MutableItemNode> level = rootChildren;
            var depth = 0;
            foreach (var itemId in chain)
            {
                if (itemId <= 0 || depth >= BuildTreeMaxDepth)
                {
                    break;
                }
                if (!level.TryGetValue(itemId, out var node))
                {
                    node = new MutableItemNode(itemId);
                    level[itemId] = node;
                }
                node.Games += row.Games;
                node.Wins += row.Wins;
                level = node.Children;
                depth++;
            }
        }

        return PruneTreeLevel(rootChildren);
    }

    // Prune one level of the tree: drop low-support nodes, cap fan-out, then
    // recurse into the kept children. Without this the payload grows with the
    // number of distinct item combinations observed, which on popular
    // champions can be hundreds of nodes the UI won't render anyway.
    private static IReadOnlyList<MutableItemNode> PruneTreeLevel(
        IDictionary<int, MutableItemNode> level)
    {
        var kept = level.Values
            .Where(node => node.Games >= BuildTreeMinGames)
            .OrderByDescending(node => node.Games)
            .ThenByDescending(node => node.Wins)
            .ThenBy(node => node.ItemId)
            .Take(BuildTreeMaxChildrenPerNode)
            .ToList();

        foreach (var node in kept)
        {
            var prunedChildren = PruneTreeLevel(node.Children);
            node.Children.Clear();
            foreach (var child in prunedChildren)
            {
                node.Children[child.ItemId] = child;
            }
        }

        return kept;
    }

    private static List<Guid> UniqueIds(IEnumerable<Guid> source)
        => source.Distinct().ToList();

    private static ChampionBuildReadModel MaterializeBuild(
        GroupAggregates aggregates,
        int totalGames,
        Dictionary<Guid, ChampionDimSpellPair> spellDims,
        Dictionary<Guid, ChampionDimSkillOrder> skillDims,
        Dictionary<Guid, ChampionDimStarterItems> starterDims,
        Dictionary<Guid, ChampionDimRunePage> runeDims)
    {
        var sliceGames = aggregates.Games;
        var spellVariations = aggregates.TopSpells
            .Select(agg => MaterializeSpell(agg, sliceGames, spellDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
        var skillVariations = aggregates.TopSkills
            .Select(agg => MaterializeSkill(agg, sliceGames, skillDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
        var starterVariations = aggregates.TopStarters
            .Select(agg => MaterializeStarter(agg, sliceGames, starterDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
        var bootsVariations = aggregates.TopBoots
            .Select(agg => MaterializeBoots(agg, sliceGames))
            .ToList();
        var runePages = aggregates.TopRunes
            .Select(agg => MaterializeRunePage(agg, sliceGames, runeDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();

        var itemPath = new BuildItemPathReadModel
        {
            ItemIds = aggregates.ItemPath,
            Games = aggregates.ItemPathGames,
            PickRate = sliceGames == 0 ? 0 : (double)aggregates.ItemPathGames / sliceGames,
            WinRate = aggregates.ItemPathGames == 0
                ? 0
                : (double)aggregates.ItemPathWins / aggregates.ItemPathGames
        };

        return new ChampionBuildReadModel
        {
            FirstItemId = aggregates.Key.FirstItemId,
            PrimaryKeystoneId = aggregates.Key.PrimaryKeystoneId,
            Games = sliceGames,
            PickRate = totalGames == 0 ? 0 : (double)sliceGames / totalGames,
            WinRate = sliceGames == 0 ? 0 : (double)aggregates.Wins / sliceGames,
            Core = new BuildCoreReadModel
            {
                ItemPath = itemPath,
                Boots = bootsVariations.FirstOrDefault(),
                StarterItems = starterVariations.FirstOrDefault(),
                SummonerSpells = spellVariations.FirstOrDefault(),
                SkillOrder = skillVariations.FirstOrDefault(),
                RunePage = runePages.FirstOrDefault()
            },
            Variations = new BuildVariationsReadModel
            {
                Boots = bootsVariations,
                StarterItems = starterVariations,
                SummonerSpells = spellVariations,
                SkillOrder = skillVariations
            },
            BuildTree = aggregates.BuildTree
                .Select(node => ConvertTreeNode(node, sliceGames))
                .ToList(),
            RunePages = runePages
        };
    }

    private static BuildSummonerSpellsReadModel? MaterializeSpell(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimSpellPair> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        return new BuildSummonerSpellsReadModel
        {
            Spell1Id = dim.Spell1Id,
            Spell2Id = dim.Spell2Id,
            Games = aggregate.Games,
            PickRate = sliceGames == 0 ? 0 : (double)aggregate.Games / sliceGames,
            WinRate = aggregate.Games == 0 ? 0 : (double)aggregate.Wins / aggregate.Games
        };
    }

    private static BuildSkillOrderReadModel? MaterializeSkill(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimSkillOrder> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        var sequence = string.IsNullOrEmpty(dim.SkillOrderKey)
            ? Array.Empty<string>()
            : dim.SkillOrderKey.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return new BuildSkillOrderReadModel
        {
            Sequence = sequence,
            Games = aggregate.Games,
            PickRate = sliceGames == 0 ? 0 : (double)aggregate.Games / sliceGames,
            WinRate = aggregate.Games == 0 ? 0 : (double)aggregate.Wins / aggregate.Games
        };
    }

    private static BuildItemSetReadModel? MaterializeStarter(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimStarterItems> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        return new BuildItemSetReadModel
        {
            ItemIds = dim.StarterItems,
            Games = aggregate.Games,
            PickRate = sliceGames == 0 ? 0 : (double)aggregate.Games / sliceGames,
            WinRate = aggregate.Games == 0 ? 0 : (double)aggregate.Wins / aggregate.Games
        };
    }

    private static BuildItemSetReadModel MaterializeBoots(BootsAggregate aggregate, int sliceGames)
        => new()
        {
            ItemIds = [aggregate.ItemId],
            Games = aggregate.Games,
            PickRate = sliceGames == 0 ? 0 : (double)aggregate.Games / sliceGames,
            WinRate = aggregate.Games == 0 ? 0 : (double)aggregate.Wins / aggregate.Games
        };

    private static BuildRunePageReadModel? MaterializeRunePage(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimRunePage> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        return new BuildRunePageReadModel
        {
            PrimaryStyleId = dim.PrimaryStyleId,
            PrimaryKeystoneId = dim.PrimaryKeystoneId,
            PrimaryPerk1Id = dim.PrimaryPerk1Id,
            PrimaryPerk2Id = dim.PrimaryPerk2Id,
            PrimaryPerk3Id = dim.PrimaryPerk3Id,
            SecondaryStyleId = dim.SecondaryStyleId,
            SecondaryPerk1Id = dim.SecondaryPerk1Id,
            SecondaryPerk2Id = dim.SecondaryPerk2Id,
            StatOffense = dim.StatOffense,
            StatFlex = dim.StatFlex,
            StatDefense = dim.StatDefense,
            Games = aggregate.Games,
            PickRate = sliceGames == 0 ? 0 : (double)aggregate.Games / sliceGames,
            WinRate = aggregate.Games == 0 ? 0 : (double)aggregate.Wins / aggregate.Games
        };
    }

    private static int MaxDepth(MutableItemNode node)
    {
        if (node.Children.Count == 0)
        {
            return 0;
        }
        var best = 0;
        foreach (var child in node.Children.Values)
        {
            var d = MaxDepth(child);
            if (d > best)
            {
                best = d;
            }
        }
        return 1 + best;
    }

    private static BuildTreeNodeReadModel ConvertTreeNode(MutableItemNode node, int parentGames)
        => new()
        {
            ItemId = node.ItemId,
            Games = node.Games,
            Wins = node.Wins,
            PickRate = parentGames == 0 ? 0 : (double)node.Games / parentGames,
            Children = node.Children.Values
                .OrderByDescending(child => child.Games)
                .ThenByDescending(child => child.Wins)
                .ThenBy(child => child.ItemId)
                .Select(child => ConvertTreeNode(child, node.Games))
                .ToList()
        };

    private readonly record struct BuildKey(int FirstItemId, int PrimaryKeystoneId);

    private sealed record PendingBuild(
        BuildKey Key,
        IReadOnlyList<ChampionPatternEnrichedRow> Rows,
        int Games,
        int Wins);

    private sealed record GroupAggregates(
        BuildKey Key,
        IReadOnlyList<ChampionPatternEnrichedRow> Rows,
        int Games,
        int Wins,
        IReadOnlyList<DimAggregate> TopSpells,
        IReadOnlyList<DimAggregate> TopSkills,
        IReadOnlyList<DimAggregate> TopStarters,
        IReadOnlyList<DimAggregate> TopRunes,
        IReadOnlyList<BootsAggregate> TopBoots,
        IReadOnlyList<int> ItemPath,
        int ItemPathGames,
        int ItemPathWins,
        IReadOnlyList<MutableItemNode> BuildTree);

    private readonly record struct DimAggregate(Guid Id, int Games, int Wins);

    private readonly record struct BootsAggregate(int ItemId, int Games, int Wins);

    private sealed record ChampionPatternEnrichedRow(
        Guid SpellPairId,
        Guid SkillOrderId,
        Guid StarterItemsId,
        Guid RunePageId,
        int BuildItem0,
        int BuildItem1,
        int BuildItem2,
        int BuildItem3,
        int BuildItem4,
        int BuildItem5,
        int BuildItem6,
        int BootsItemId,
        int PrimaryKeystoneId,
        int Games,
        int Wins);

    private sealed class MutableItemNode(int itemId)
    {
        public int ItemId { get; } = itemId;

        public int Games { get; set; }

        public int Wins { get; set; }

        public Dictionary<int, MutableItemNode> Children { get; } = [];
    }
}
