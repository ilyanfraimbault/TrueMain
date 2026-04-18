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
            StarterItemOptions = ChampionStarterItemAggregator.AggregateTopThree(rows, sampleSize),
            SummonerSpellOptions = ChampionSummonerSpellAggregator.AggregateTopThree(rows, sampleSize),
            SkillOrderOptions = ChampionSkillOrderAggregator.AggregateTopThree(rows, sampleSize)
        };
    }

    public static IReadOnlyList<ChampionCorrelatedPatternReadModel> BuildCorrelatedPatterns(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize)
        => ChampionPatternProjector.Project(rows, sampleSize);

    public static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;
}
