using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionOptionProjector
{
    public static ChampionAdvancedDetailsReadModel BuildAdvancedDetails(
        IReadOnlyCollection<ChampionAggregateStarterItems> starterItems,
        IReadOnlyCollection<ChampionAggregateSpellPair> spellPairs,
        IReadOnlyCollection<ChampionAggregateSkillOrder> skillOrders,
        int sampleSize)
        => new()
        {
            SampleSize = sampleSize,
            StarterItemOptions = ChampionStarterItemAggregator.AggregateTopThree(starterItems, sampleSize),
            SummonerSpellOptions = ChampionSummonerSpellAggregator.AggregateTopThree(spellPairs, sampleSize),
            SkillOrderOptions = ChampionSkillOrderAggregator.AggregateTopThree(skillOrders, sampleSize)
        };

    public static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;
}
