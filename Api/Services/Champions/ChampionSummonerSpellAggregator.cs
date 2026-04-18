using Core.Lol.Spells;
using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionSummonerSpellAggregator
{
    public static IReadOnlyList<SummonerSpellOptionReadModel> AggregateTopThree(
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
                    PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.Spell1Id)
            .ThenBy(option => option.Spell2Id)
            .Take(3)
            .ToList();
}
