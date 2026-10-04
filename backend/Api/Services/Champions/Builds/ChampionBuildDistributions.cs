using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Builds;

/// <summary>
/// The pure half of the champion build page read: folds the scope's aggregate pattern
/// rows into builds, keyed by (first completed item, primary keystone), and inside each
/// build the per-dimension distributions (summoner spells, skill order, starter items,
/// boots, rune pages) plus the build tree and its highlighted item path.
///
/// <para>
/// <see cref="ChampionBuildsQueryService"/> owns the database side: it loads the rows,
/// calls <see cref="Fold"/>, resolves the dimension ids the folded builds reference, and
/// hands those lookups to <see cref="Materialize"/>. Kept free of the database so the
/// arithmetic is testable on its own.
/// </para>
/// </summary>
internal static class ChampionBuildDistributions
{
    // Build tabs and per-dimension variations are shared with the live matchup
    // fold — both feed the same panel, see ChampionBuildDisplayCaps.
    private const int MaxBuilds = ChampionBuildDisplayCaps.MaxBuilds;
    private const double MinBuildPickRate = 0.05;
    private const int VariationsTopN = ChampionBuildDisplayCaps.MaxVariations;
    private const int RunePagesTopN = 3;

    /// <summary>
    /// Groups <paramref name="rows"/> into builds, keeps those above the pick-rate floor
    /// (most played first, capped at <see cref="ChampionBuildDisplayCaps.MaxBuilds"/>) and
    /// computes each build's distributions.
    /// </summary>
    public static IReadOnlyList<GroupAggregates> Fold(
        IReadOnlyList<ChampionPatternEnrichedRow> rows,
        int totalGames)
        => rows
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
            .Select(AggregateGroup)
            .ToList();

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

        // Build the (pruned) tree once and derive the highlighted item path
        // from the same tree, so anything the path includes is guaranteed to
        // be visible in the build-tree visualization (no "ghost" deep items).
        var sequences = rows
            .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                row.BuildItem1, row.BuildItem2, row.BuildItem3,
                row.BuildItem4, row.BuildItem5, row.BuildItem6,
                row.Games, row.Wins))
            .ToList();
        var buildTree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames);
        var (itemPath, itemPathGames, itemPathWins) = ChampionBuildPathAnalyzer.WalkPath(
            buildTree, pending.Key.FirstItemId, sliceGames, pending.Wins);

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

    public static ChampionBuildReadModel Materialize(
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
            PickRate = RateMath.Rate(aggregates.ItemPathGames, sliceGames),
            WinRate = RateMath.Rate(aggregates.ItemPathWins, aggregates.ItemPathGames)
        };

        return new ChampionBuildReadModel
        {
            FirstItemId = aggregates.Key.FirstItemId,
            PrimaryKeystoneId = aggregates.Key.PrimaryKeystoneId,
            Games = sliceGames,
            PickRate = RateMath.Rate(sliceGames, totalGames),
            WinRate = RateMath.Rate(aggregates.Wins, sliceGames),
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
                .Select(node => ChampionBuildPathAnalyzer.ToReadModel(node, sliceGames))
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
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
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
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
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
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };
    }

    private static BuildItemSetReadModel MaterializeBoots(BootsAggregate aggregate, int sliceGames)
        => new()
        {
            ItemIds = [aggregate.ItemId],
            Games = aggregate.Games,
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
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
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };
    }

    internal readonly record struct BuildKey(int FirstItemId, int PrimaryKeystoneId);

    private sealed record PendingBuild(
        BuildKey Key,
        IReadOnlyList<ChampionPatternEnrichedRow> Rows,
        int Games,
        int Wins);

    internal sealed record GroupAggregates(
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
        IReadOnlyList<ChampionBuildPathAnalyzer.TreeNode> BuildTree);

    internal readonly record struct DimAggregate(Guid Id, int Games, int Wins);

    internal readonly record struct BootsAggregate(int ItemId, int Games, int Wins);

    internal sealed record ChampionPatternEnrichedRow(
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
}
