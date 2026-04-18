using Core.Lol.Spells;
using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionOptionProjector
{
    public static ChampionAdvancedDetailsReadModel BuildAdvancedDetails(IReadOnlyCollection<ChampionPatternAggregate> rows)
    {
        var sampleSize = rows.Sum(row => row.Games);

        return new ChampionAdvancedDetailsReadModel
        {
            SampleSize = sampleSize,
            StarterItemOptions = BuildItemSetOptions(rows, sampleSize, BuildStarterItemSet),
            SummonerSpellOptions = BuildSummonerSpellOptions(rows, sampleSize),
            SkillOrderOptions = BuildSkillOrderOptions(rows, sampleSize)
        };
    }

    public static IReadOnlyList<ChampionCorrelatedPatternReadModel> BuildCorrelatedPatterns(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize)
        => rows
            .Select(row =>
            {
                var starterItems = BuildStarterItemSet(row);
                var buildItemIds = BuildFinalBuildItemSet(row);
                var displayPair = new SummonerSpellPair(row.SummonerSpell1Id, row.SummonerSpell2Id).OrderedForDisplay();

                return new
                {
                    row.Games,
                    row.Wins,
                    row.AggregatedAtUtc,
                    StarterItems = starterItems,
                    row.BootsItemId,
                    BuildItemIds = buildItemIds,
                    SummonerSpell1Id = displayPair.Spell1Id,
                    SummonerSpell2Id = displayPair.Spell2Id,
                    row.SkillOrderKey
                };
            })
            .GroupBy(entry => new
            {
                StarterItemsKey = string.Join("-", entry.StarterItems),
                entry.BootsItemId,
                BuildKey = string.Join("-", entry.BuildItemIds),
                entry.SummonerSpell1Id,
                entry.SummonerSpell2Id,
                entry.SkillOrderKey
            })
            .Select(group =>
            {
                var first = group.First();
                var games = group.Sum(entry => entry.Games);
                var wins = group.Sum(entry => entry.Wins);
                var lastUpdatedAtUtc = group.Max(entry => entry.AggregatedAtUtc);

                return new ChampionCorrelatedPatternReadModel
                {
                    StarterItems = first.StarterItems.Count == 0
                        ? null
                        : new ItemSetOptionReadModel
                        {
                            ItemIds = first.StarterItems,
                            Games = games,
                            PlayRate = ComputeRate(games, sampleSize),
                            WinRate = ComputeRate(wins, games)
                        },
                    Boots = first.BootsItemId <= 0
                        ? null
                        : new ItemSetOptionReadModel
                        {
                            ItemIds = [first.BootsItemId],
                            Games = games,
                            PlayRate = ComputeRate(games, sampleSize),
                            WinRate = ComputeRate(wins, games)
                        },
                    BuildItemIds = first.BuildItemIds,
                    SummonerSpells = new SummonerSpellOptionReadModel
                    {
                        Spell1Id = first.SummonerSpell1Id,
                        Spell2Id = first.SummonerSpell2Id,
                        Games = games,
                        PlayRate = ComputeRate(games, sampleSize),
                        WinRate = ComputeRate(wins, games)
                    },
                    SkillOrder = new SkillOrderOptionReadModel
                    {
                        Sequence = SplitSequence(first.SkillOrderKey),
                        Games = games,
                        PlayRate = ComputeRate(games, sampleSize),
                        WinRate = ComputeRate(wins, games)
                    },
                    Games = games,
                    Wins = wins,
                    LastUpdatedAtUtc = lastUpdatedAtUtc
                };
            })
            .OrderByDescending(pattern => pattern.Games)
            .ThenByDescending(pattern => pattern.Wins)
            .ThenByDescending(pattern => pattern.LastUpdatedAtUtc)
            .ThenBy(pattern => pattern.SummonerSpells.Spell1Id)
            .ThenBy(pattern => pattern.SummonerSpells.Spell2Id)
            .ThenBy(pattern => string.Join("-", pattern.SkillOrder.Sequence), StringComparer.Ordinal)
            .ThenBy(pattern => string.Join("-", pattern.BuildItemIds), StringComparer.Ordinal)
            .ThenBy(pattern => pattern.StarterItems is null
                ? string.Empty
                : string.Join("-", pattern.StarterItems.ItemIds), StringComparer.Ordinal)
            .ThenBy(pattern => pattern.Boots is null ? 0 : pattern.Boots.ItemIds[0])
            .ToList();

    public static IReadOnlyList<int> BuildStarterItemSet(ChampionPatternAggregate aggregate)
        => aggregate.StarterItems
            .Where(itemId => itemId > 0)
            .ToList();

    public static IReadOnlyList<int> BuildFinalBuildItemSet(ChampionPatternAggregate aggregate)
        => new[]
        {
            aggregate.BuildItem0,
            aggregate.BuildItem1,
            aggregate.BuildItem2,
            aggregate.BuildItem3,
            aggregate.BuildItem4,
            aggregate.BuildItem5,
            aggregate.BuildItem6
        }
        .Where(itemId => itemId > 0)
        .ToList();

    public static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;

    public static IReadOnlyList<string> SplitSequence(string sequenceKey)
        => string.IsNullOrWhiteSpace(sequenceKey)
            ? []
            : sequenceKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<ItemSetOptionReadModel> BuildItemSetOptions(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize,
        Func<ChampionPatternAggregate, IReadOnlyList<int>> itemSetSelector)
        => rows
            .Select(row => new
            {
                row.Games,
                row.Wins,
                ItemSet = itemSetSelector(row)
            })
            .Where(entry => entry.ItemSet.Count > 0)
            .GroupBy(entry => string.Join("-", entry.ItemSet))
            .Select(group =>
            {
                var itemSet = group.First().ItemSet;
                var games = group.Sum(entry => entry.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = itemSet,
                    Games = games,
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(entry => entry.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();

    private static IReadOnlyList<SummonerSpellOptionReadModel> BuildSummonerSpellOptions(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize)
        => rows
            .GroupBy(row => new SummonerSpellPair(row.SummonerSpell1Id, row.SummonerSpell2Id).OrderedForDisplay())
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                return new SummonerSpellOptionReadModel
                {
                    Spell1Id = group.Key.Spell1Id,
                    Spell2Id = group.Key.Spell2Id,
                    Games = games,
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.Spell1Id)
            .ThenBy(option => option.Spell2Id)
            .Take(3)
            .ToList();

    private static IReadOnlyList<SkillOrderOptionReadModel> BuildSkillOrderOptions(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.SkillOrderKey))
            .GroupBy(row => row.SkillOrderKey)
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                return new SkillOrderOptionReadModel
                {
                    Sequence = SplitSequence(group.Key),
                    Games = games,
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.Sequence), StringComparer.Ordinal)
            .Take(3)
            .ToList();
}
