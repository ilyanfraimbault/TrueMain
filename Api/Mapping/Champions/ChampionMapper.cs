using TrueMain.Contracts.Champions;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Mapping.Champions;

public static class ChampionMapper
{
    public static ChampionResponse ToContract(
        ChampionFoundationReadModel foundationReadModel,
        ChampionCoreReadModel coreReadModel,
        ChampionBuildTreeReadModel buildTreeReadModel)
        => new()
        {
            Summary = new ChampionSummaryResponse
            {
                ChampionId = foundationReadModel.Summary.ChampionId,
                Games = foundationReadModel.Summary.Games,
                WinRate = foundationReadModel.Summary.WinRate,
                TrueMainCount = foundationReadModel.Summary.TrueMainCount,
                Position = foundationReadModel.Summary.Position,
                LatestPatchVersion = foundationReadModel.Summary.LatestPatchVersion,
                LastUpdatedAtUtc = foundationReadModel.Summary.LastUpdatedAtUtc
            },
            Core = new ChampionCoreResponse
            {
                SampleSize = coreReadModel.SampleSize,
                StarterItems = coreReadModel.StarterItems is null
                    ? null
                    : MapItemSetOption(coreReadModel.StarterItems),
                Boots = coreReadModel.Boots is null
                    ? null
                    : MapItemSetOption(coreReadModel.Boots),
                BuildPath = coreReadModel.BuildPathItemIds.Count == 0
                    ? null
                    : new BuildPathPreviewResponse
                    {
                        ItemIds = coreReadModel.BuildPathItemIds
                    },
                SummonerSpells = coreReadModel.SummonerSpells is null
                    ? null
                    : MapSummonerOption(coreReadModel.SummonerSpells),
                SkillOrder = coreReadModel.SkillOrder is null
                    ? null
                    : MapSkillOrderOption(coreReadModel.SkillOrder)
            },
            Advanced = new ChampionAdvancedDetailsResponse
            {
                StarterItemOptions = foundationReadModel.Advanced.StarterItemOptions
                    .Select(MapItemSetOption)
                    .ToList(),
                SummonerSpellOptions = foundationReadModel.Advanced.SummonerSpellOptions
                    .Select(MapSummonerOption)
                    .ToList(),
                SkillOrderOptions = foundationReadModel.Advanced.SkillOrderOptions
                    .Select(MapSkillOrderOption)
                    .ToList()
            },
            BuildTree = buildTreeReadModel.ToContract()
        };

    private static SummonerSpellOptionResponse MapSummonerOption(SummonerSpellOptionReadModel readModel)
        => new()
        {
            Spell1Id = readModel.Spell1Id,
            Spell2Id = readModel.Spell2Id,
            Games = readModel.Games,
            PlayRate = readModel.PlayRate,
            WinRate = readModel.WinRate
        };

    private static SkillOrderOptionResponse MapSkillOrderOption(SkillOrderOptionReadModel readModel)
        => new()
        {
            Sequence = readModel.Sequence.ToList(),
            Games = readModel.Games,
            PlayRate = readModel.PlayRate,
            WinRate = readModel.WinRate
        };

    private static ItemSetOptionResponse MapItemSetOption(ItemSetOptionReadModel readModel)
        => new()
        {
            ItemIds = readModel.ItemIds.ToList(),
            Games = readModel.Games,
            PlayRate = readModel.PlayRate,
            WinRate = readModel.WinRate
        };
}
