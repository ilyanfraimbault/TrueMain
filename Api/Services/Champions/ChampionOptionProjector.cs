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
            StarterItemOptions = BuildItemSetOptions(rows, sampleSize, BuildStarterItemSet),
            SummonerSpellOptions = BuildSummonerSpellOptions(rows, sampleSize),
            SkillOrderOptions = BuildSkillOrderOptions(rows, sampleSize)
        };
    }

    public static IReadOnlyList<int> BuildStarterItemSet(ChampionPatternAggregate aggregate)
        => aggregate.StarterItems
            .Where(itemId => itemId > 0)
            .ToList();

    public static (int spell1Id, int spell2Id) NormalizeSummonerPair(int summoner1Id, int summoner2Id)
    {
        const int FlashId = 4;
        const int SmiteId = 11;

        if (summoner1Id == FlashId || summoner2Id == FlashId)
        {
            return summoner1Id == FlashId
                ? (summoner1Id, summoner2Id)
                : (summoner2Id, summoner1Id);
        }

        if (summoner1Id == SmiteId || summoner2Id == SmiteId)
        {
            return summoner1Id == SmiteId
                ? (summoner1Id, summoner2Id)
                : (summoner2Id, summoner1Id);
        }

        return summoner1Id <= summoner2Id
            ? (summoner1Id, summoner2Id)
            : (summoner2Id, summoner1Id);
    }

    public static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;

    public static IReadOnlyList<string> SplitSequence(string sequenceKey)
        => string.IsNullOrWhiteSpace(sequenceKey)
            ? []
            : sequenceKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<ItemSetOptionReadModel> BuildItemSetOptions(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize,
        Func<ChampionPatternAggregate, IReadOnlyList<int>> itemSetSelector)
        => rows
            .Select(row => new
            {
                row.Games,
                row.Wins,
                ItemSet = itemSetSelector(row)
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
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(entry => entry.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();

    private static IReadOnlyList<SummonerSpellOptionReadModel> BuildSummonerSpellOptions(
        IReadOnlyCollection<ChampionPatternAggregate> rows,
        int sampleSize)
        => rows
            .GroupBy(row => NormalizeSummonerPair(row.SummonerSpell1Id, row.SummonerSpell2Id))
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                return new SummonerSpellOptionReadModel
                {
                    Spell1Id = group.Key.spell1Id,
                    Spell2Id = group.Key.spell2Id,
                    Games = games,
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.Spell1Id)
            .ThenBy(option => option.Spell2Id)
            .Take(3)
            .ToList();

    private static IReadOnlyList<SkillOrderOptionReadModel> BuildSkillOrderOptions(
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
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(row => row.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.Sequence), StringComparer.Ordinal)
            .Take(3)
            .ToList();
}
