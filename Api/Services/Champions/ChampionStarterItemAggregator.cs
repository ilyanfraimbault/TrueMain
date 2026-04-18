using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionStarterItemAggregator
{
    public static IReadOnlyList<ItemSetOptionReadModel> AggregateTopThree(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize)
        => rows
            .Select(row => new
            {
                row.Games,
                row.Wins,
                ItemSet = ChampionPatternProjector.BuildStarterItemSet(row)
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
                    PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(entry => entry.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();
}
