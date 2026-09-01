using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Folds raw participant facts into the champion page's own shape — builds, each with a
/// core and its variations, plus the item tree (#923).
///
/// <para>
/// The offline pipeline produces this shape by aggregating into <c>champion_dim_*</c> and
/// <c>champion_aggregate_patterns</c>. Those aggregates carry no opponent dimension, so a
/// matchup-scoped page cannot read them and folds the same facts in memory instead. This
/// is that fold, kept pure so the arithmetic — which is what a reader will argue with —
/// is testable without a database.
/// </para>
///
/// <para>
/// <b>The core is derived, never assumed.</b> It is the most common value of each
/// dimension <em>within the slice being folded</em>, so a matchup-filtered slice yields a
/// matchup-specific core. Deriving it from anything else would let the page show a core
/// build that none of the variations beside it support.
/// </para>
///
/// <para>
/// <b>No sample floor</b> (decided 2026-07-30). Measured on production, the median
/// champion-vs-opponent pair holds 4 games on a patch and only 24% reach 20, so most
/// matchup slices are thin. Every variation therefore carries its own game count and the
/// caller renders it: the honest answer to a thin sample is to show how thin it is, not
/// to hide the section or to silently widen it.
/// </para>
/// </summary>
internal static class LiveBuildVariationAggregator
{
    /// <summary>Variations listed per dimension — <see cref="ChampionBuildDisplayCaps.MaxVariations"/>.</summary>
    private const int MaxVariations = ChampionBuildDisplayCaps.MaxVariations;

    /// <summary>Core path depth: the first completed legendaries that define a build.</summary>
    private const int CorePathLength = 3;

    /// <summary>Builds listed for a slice, most played first — <see cref="ChampionBuildDisplayCaps.MaxBuilds"/>.</summary>
    private const int MaxBuilds = ChampionBuildDisplayCaps.MaxBuilds;

    public static IReadOnlyList<ChampionBuildReadModel> Aggregate(IReadOnlyList<CompositionParticipantFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Count == 0)
        {
            return [];
        }

        var sliceGames = facts.Count;

        // A "build" is keyed the way the aggregate path keys it: the first completed item
        // and the keystone. Facts missing either cannot be placed on that grid — they
        // still counted in sliceGames, so the pick rates below stay honest about the
        // whole slice rather than about the placeable part of it.
        return facts
            .Where(fact => fact.BuildItems.Count > 0 && fact.RunePage is not null)
            .GroupBy(fact => (FirstItemId: fact.BuildItems[0], fact.RunePage!.PrimaryKeystoneId))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.FirstItemId)
            .Take(MaxBuilds)
            .Select(group => BuildFor(group.Key.FirstItemId, group.Key.PrimaryKeystoneId, [.. group], sliceGames))
            .ToList();
    }

    private static ChampionBuildReadModel BuildFor(
        int firstItemId,
        int keystoneId,
        IReadOnlyList<CompositionParticipantFacts> facts,
        int sliceGames)
    {
        var games = facts.Count;
        var wins = facts.Count(fact => fact.Win);

        var runePages = TopVariations(
            facts.Where(fact => fact.RunePage is not null),
            fact => fact.RunePage!,
            games,
            (page, groupGames, groupWins, denominator) => ToRunePage(page, groupGames, groupWins, denominator));

        var boots = TopVariations(
            facts.Where(fact => fact.BootsItemId > 0),
            fact => fact.BootsItemId,
            games,
            (itemId, groupGames, groupWins, denominator) => ToItemSet([itemId], groupGames, groupWins, denominator));

        var starters = TopVariations(
            facts.Where(fact => fact.StarterItems.Count > 0),
            fact => new ItemList(fact.StarterItems),
            games,
            (items, groupGames, groupWins, denominator) => ToItemSet(items.Items, groupGames, groupWins, denominator));

        var spells = TopVariations(
            facts.Where(fact => fact.Spell1Id > 0 && fact.Spell2Id > 0),
            fact => (fact.Spell1Id, fact.Spell2Id),
            games,
            (pair, groupGames, groupWins, denominator) => new BuildSummonerSpellsReadModel
            {
                Spell1Id = pair.Spell1Id,
                Spell2Id = pair.Spell2Id,
                Games = groupGames,
                PickRate = RateMath.Rate(groupGames, denominator),
                WinRate = RateMath.Rate(groupWins, groupGames),
            });

        var skillOrders = TopVariations(
            facts.Where(fact => !string.IsNullOrEmpty(fact.SkillOrderKey)),
            fact => fact.SkillOrderKey,
            games,
            (key, groupGames, groupWins, denominator) => new BuildSkillOrderReadModel
            {
                // Same '-' separated key the dimension table stores, split the same way
                // ChampionBuildsQueryService splits it.
                Sequence = key.Split('-', StringSplitOptions.RemoveEmptyEntries),
                Games = groupGames,
                PickRate = RateMath.Rate(groupGames, denominator),
                WinRate = RateMath.Rate(groupWins, groupGames),
            });

        var itemPaths = TopVariations(
            facts.Where(fact => fact.BuildItems.Count > 0),
            fact => new ItemList([.. fact.BuildItems.Take(CorePathLength)]),
            games,
            (items, groupGames, groupWins, denominator) => new BuildItemPathReadModel
            {
                ItemIds = items.Items,
                Games = groupGames,
                PickRate = RateMath.Rate(groupGames, denominator),
                WinRate = RateMath.Rate(groupWins, groupGames),
            });

        return new ChampionBuildReadModel
        {
            FirstItemId = firstItemId,
            PrimaryKeystoneId = keystoneId,
            Games = games,
            // Against the whole slice, so the builds of a matchup add up to how much of
            // that matchup they cover.
            PickRate = RateMath.Rate(games, sliceGames),
            WinRate = RateMath.Rate(wins, games),
            Core = new BuildCoreReadModel
            {
                // The most played of each dimension in this build — recomputed here, so
                // a matchup slice moves the core with it.
                ItemPath = itemPaths.FirstOrDefault(),
                Boots = boots.FirstOrDefault(),
                StarterItems = starters.FirstOrDefault(),
                SummonerSpells = spells.FirstOrDefault(),
                SkillOrder = skillOrders.FirstOrDefault(),
                RunePage = runePages.FirstOrDefault(),
            },
            RunePages = runePages,
            Variations = new BuildVariationsReadModel
            {
                Boots = boots,
                StarterItems = starters,
                SummonerSpells = spells,
                SkillOrder = skillOrders,
            },
            BuildTree = BuildTree(facts, games),
        };
    }

    private static IReadOnlyList<BuildTreeNodeReadModel> BuildTree(
        IReadOnlyList<CompositionParticipantFacts> facts,
        int games)
    {
        // The tree builder takes observed progressions with their own games/wins, the
        // same rows the aggregate path feeds it from champion_dim_builds — so grouping
        // identical chains here reproduces that input exactly.
        var sequences = facts
            .Where(fact => fact.BuildItems.Count > 1)
            .GroupBy(fact => new ItemList([.. fact.BuildItems.Skip(1).Take(ChampionBuildPathAnalyzer.BuildTreeMaxDepth)]))
            .Select(group => new ChampionBuildPathAnalyzer.BuildSequence(
                ItemAt(group.Key.Items, 0),
                ItemAt(group.Key.Items, 1),
                ItemAt(group.Key.Items, 2),
                ItemAt(group.Key.Items, 3),
                ItemAt(group.Key.Items, 4),
                ItemAt(group.Key.Items, 5),
                group.Count(),
                group.Count(fact => fact.Win)))
            .ToList();

        return
        [
            .. ChampionBuildPathAnalyzer
                .BuildItemTree(sequences, games)
                .Select(node => ChampionBuildPathAnalyzer.ToReadModel(node, games))
        ];
    }

    private static int ItemAt(IReadOnlyList<int> items, int index) => index < items.Count ? items[index] : 0;

    /// <summary>
    /// Top <see cref="MaxVariations"/> groups of one dimension, most played first, each
    /// carrying its own games so a one-game variation cannot pass for a trend.
    /// </summary>
    private static IReadOnlyList<TModel> TopVariations<TKey, TModel>(
        IEnumerable<CompositionParticipantFacts> eligible,
        Func<CompositionParticipantFacts, TKey> keySelector,
        int denominator,
        Func<TKey, int, int, int, TModel> toModel)
        where TKey : notnull
    {
        return
        [
            .. eligible
                .GroupBy(keySelector)
                .Select(group => new
                {
                    group.Key,
                    Games = group.Count(),
                    Wins = group.Count(fact => fact.Win),
                })
                .OrderByDescending(group => group.Games)
                .ThenByDescending(group => group.Wins)
                .Take(MaxVariations)
                .Select(group => toModel(group.Key, group.Games, group.Wins, denominator))
        ];
    }

    private static BuildRunePageReadModel ToRunePage(
        CompositionRunePageFacts page,
        int games,
        int wins,
        int denominator) => new()
        {
            PrimaryStyleId = page.PrimaryStyleId,
            PrimaryKeystoneId = page.PrimaryKeystoneId,
            PrimaryPerk1Id = page.PrimaryPerk1Id,
            PrimaryPerk2Id = page.PrimaryPerk2Id,
            PrimaryPerk3Id = page.PrimaryPerk3Id,
            SecondaryStyleId = page.SecondaryStyleId,
            SecondaryPerk1Id = page.SecondaryPerk1Id,
            SecondaryPerk2Id = page.SecondaryPerk2Id,
            StatOffense = page.StatOffense,
            StatFlex = page.StatFlex,
            StatDefense = page.StatDefense,
            Games = games,
            PickRate = RateMath.Rate(games, denominator),
            WinRate = RateMath.Rate(wins, games),
        };

    private static BuildItemSetReadModel ToItemSet(
        IReadOnlyList<int> itemIds,
        int games,
        int wins,
        int denominator) => new()
        {
            ItemIds = itemIds,
            Games = games,
            PickRate = RateMath.Rate(games, denominator),
            WinRate = RateMath.Rate(wins, games),
        };

    /// <summary>
    /// Value-equality wrapper over an item list, so grouping keys on the sequence rather
    /// than on the list instance.
    /// </summary>
    private readonly record struct ItemList(IReadOnlyList<int> Items)
    {
        public bool Equals(ItemList other) => Items.SequenceEqual(other.Items);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var item in Items)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }
}
