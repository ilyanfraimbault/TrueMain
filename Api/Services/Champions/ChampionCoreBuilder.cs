using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionCoreBuilder
{
    public static ChampionCoreReadModel Build(
        ChampionFoundationReadModel foundationReadModel,
        bool includeBuildPath = true)
    {
        var primaryStarterItems = foundationReadModel.Advanced.StarterItemOptions.FirstOrDefault();
        var correlatedPattern = foundationReadModel.CorrelatedPatterns
            .FirstOrDefault(pattern => pattern.BuildItemIds.Count > 0)
            ?? foundationReadModel.CorrelatedPatterns.FirstOrDefault();

        return new ChampionCoreReadModel
        {
            SampleSize = foundationReadModel.Advanced.SampleSize,
            StarterItems = primaryStarterItems,
            Boots = correlatedPattern?.Boots,
            BuildPathItemIds = includeBuildPath
                ? correlatedPattern?.BuildItemIds.Take(3).ToList() ?? []
                : [],
            SummonerSpells = correlatedPattern?.SummonerSpells,
            SkillOrder = correlatedPattern?.SkillOrder
        };
    }
}
