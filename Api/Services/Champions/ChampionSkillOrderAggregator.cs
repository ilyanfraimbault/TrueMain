using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionSkillOrderAggregator
{
    public static IReadOnlyList<SkillOrderOptionReadModel> AggregateTopThree(
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
                    PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
                    WinRate = ChampionOptionProjector.ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.Sequence), StringComparer.Ordinal)
            .Take(3)
            .ToList();

    public static IReadOnlyList<string> SplitSequence(string sequenceKey)
        => string.IsNullOrWhiteSpace(sequenceKey)
            ? []
            : sequenceKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
