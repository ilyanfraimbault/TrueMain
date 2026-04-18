using Core.Lol.Spells;
using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionPatternProjector
{
    public static IReadOnlyList<ChampionCorrelatedPatternReadModel> Project(
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
                    Games = games,
                    Wins = wins,
                    LastUpdatedAtUtc = lastUpdatedAtUtc,
                    StarterItems = first.StarterItems.Count == 0
                        ? null
                        : new ItemSetOptionReadModel
                        {
                            ItemIds = first.StarterItems,
                            Games = games,
                            PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                            WinRate = ChampionOptionProjector.ComputeRate(wins, games)
                        },
                    Boots = first.BootsItemId > 0
                        ? new ItemSetOptionReadModel
                        {
                            ItemIds = [first.BootsItemId],
                            Games = games,
                            PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                            WinRate = ChampionOptionProjector.ComputeRate(wins, games)
                        }
                        : null,
                    BuildItemIds = first.BuildItemIds,
                    SummonerSpells = new SummonerSpellOptionReadModel
                    {
                        Spell1Id = first.SummonerSpell1Id,
                        Spell2Id = first.SummonerSpell2Id,
                        Games = games,
                        PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                        WinRate = ChampionOptionProjector.ComputeRate(wins, games)
                    },
                    SkillOrder = new SkillOrderOptionReadModel
                    {
                        Sequence = ChampionSkillOrderAggregator.SplitSequence(first.SkillOrderKey),
                        Games = games,
                        PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                        WinRate = ChampionOptionProjector.ComputeRate(wins, games)
                    }
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
}
