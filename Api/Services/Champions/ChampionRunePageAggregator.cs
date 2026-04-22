using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionRunePageAggregator
{
    public static IReadOnlyList<RunePageOptionReadModel> AggregateTopThree(
        IReadOnlyCollection<ChampionAggregateRunePage> rows,
        int sampleSize)
        => rows
            .GroupBy(row => (
                row.PrimaryStyleId,
                row.PrimaryKeystoneId,
                row.PrimaryPerk1Id,
                row.PrimaryPerk2Id,
                row.PrimaryPerk3Id,
                row.SecondaryStyleId,
                row.SecondaryPerk1Id,
                row.SecondaryPerk2Id,
                row.StatOffense,
                row.StatFlex,
                row.StatDefense))
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                return new RunePageOptionReadModel
                {
                    PrimaryStyleId = group.Key.PrimaryStyleId,
                    PrimaryKeystoneId = group.Key.PrimaryKeystoneId,
                    PrimaryPerk1Id = group.Key.PrimaryPerk1Id,
                    PrimaryPerk2Id = group.Key.PrimaryPerk2Id,
                    PrimaryPerk3Id = group.Key.PrimaryPerk3Id,
                    SecondaryStyleId = group.Key.SecondaryStyleId,
                    SecondaryPerk1Id = group.Key.SecondaryPerk1Id,
                    SecondaryPerk2Id = group.Key.SecondaryPerk2Id,
                    StatOffense = group.Key.StatOffense,
                    StatFlex = group.Key.StatFlex,
                    StatDefense = group.Key.StatDefense,
                    Games = games,
                    PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.PrimaryStyleId)
            .ThenBy(option => option.PrimaryKeystoneId)
            .Take(3)
            .ToList();
}
