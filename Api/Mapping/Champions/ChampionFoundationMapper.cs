using TrueMain.Contracts.Champions;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Mapping.Champions;

public static class ChampionFoundationMapper
{
    public static ChampionFoundationResponse ToContract(this ChampionFoundationReadModel readModel)
    {
        return new ChampionFoundationResponse
        {
            Summary = new ChampionSummaryResponse
            {
                ChampionId = readModel.Summary.ChampionId,
                Games = readModel.Summary.Games,
                WinRate = readModel.Summary.WinRate,
                SpecialistCount = readModel.Summary.SpecialistCount,
                OtpCount = readModel.Summary.OtpCount,
                PrimaryPosition = readModel.Summary.PrimaryPosition,
                LatestPatchVersion = readModel.Summary.LatestPatchVersion,
                LastUpdatedAtUtc = readModel.Summary.LastUpdatedAtUtc
            },
            HowToPlay = new ChampionHowToPlayFoundationResponse
            {
                SampleSize = readModel.HowToPlay.SampleSize,
                CoreSummonerSpells = readModel.HowToPlay.CoreSummonerSpells is null
                    ? null
                    : MapSummonerOption(readModel.HowToPlay.CoreSummonerSpells),
                CoreSkillOrder = readModel.HowToPlay.CoreSkillOrder is null
                    ? null
                    : MapSkillOrderOption(readModel.HowToPlay.CoreSkillOrder),
                CoreItemSet = readModel.HowToPlay.CoreItemSet is null
                    ? null
                    : MapItemSetOption(readModel.HowToPlay.CoreItemSet),
                SummonerSpellOptions = readModel.HowToPlay.SummonerSpellOptions
                    .Select(MapSummonerOption)
                    .ToList(),
                SkillOrderOptions = readModel.HowToPlay.SkillOrderOptions
                    .Select(MapSkillOrderOption)
                    .ToList(),
                ItemSetOptions = readModel.HowToPlay.ItemSetOptions
                    .Select(MapItemSetOption)
                    .ToList()
            }
        };
    }

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
