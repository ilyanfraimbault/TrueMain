using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionStarterItemAggregator
{
    public static IReadOnlyList<ItemSetOptionReadModel> AggregateTopThree(
        IReadOnlyCollection<ChampionAggregateStarterItems> rows,
        int sampleSize)
        => rows
            .Where(row => row.StarterItems.Count > 0)
            .GroupBy(row => row.StarterItemsKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = group.First().StarterItems,
                    Games = games,
                    PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();
}
