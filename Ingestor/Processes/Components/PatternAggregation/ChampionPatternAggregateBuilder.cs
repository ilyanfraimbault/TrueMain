using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternAggregateBuilder(
    IItemMetadataProvider itemMetadataProvider)
{
    internal async Task<ChampionPatternAggregationResult> BuildAggregatesAsync(
        IReadOnlyCollection<AggregateSourceRow> sourceRows,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var expandedRows = await ExpandSourceRowsAsync(sourceRows, ct);

        return new ChampionPatternAggregationResult
        {
            SourceRowCount = sourceRows.Count,
            AggregateRows = expandedRows
                .GroupBy(row => new AggregateKey(
                    row.RiotAccountId,
                    row.ChampionId,
                    row.GameVersion,
                    row.PlatformId,
                    row.QueueId,
                    row.Position,
                    row.PrimaryStyleId,
                    row.SubStyleId,
                    row.PerksOffense,
                    row.PerksFlex,
                    row.PerksDefense,
                    row.SummonerSpell1Id,
                    row.SummonerSpell2Id,
                    row.SkillOrderKey,
                    row.StarterItemsKey,
                    row.BootsItemId,
                    row.BuildItem0,
                    row.BuildItem1,
                    row.BuildItem2,
                    row.BuildItem3,
                    row.BuildItem4,
                    row.BuildItem5,
                    row.BuildItem6))
                .OrderBy(group => group.Key.RiotAccountId)
                .ThenBy(group => group.Key.ChampionId)
                .ThenBy(group => group.Key.GameVersion)
                .ThenBy(group => group.Key.PlatformId)
                .ThenBy(group => group.Key.Position)
                .Select(group => new ChampionPatternAggregate
                {
                    Id = Guid.NewGuid(),
                    RiotAccountId = group.Key.RiotAccountId,
                    ChampionId = group.Key.ChampionId,
                    GameVersion = group.Key.GameVersion,
                    PlatformId = group.Key.PlatformId,
                    QueueId = group.Key.QueueId,
                    Position = group.Key.Position,
                    PrimaryStyleId = group.Key.PrimaryStyleId,
                    SubStyleId = group.Key.SubStyleId,
                    PerksOffense = group.Key.PerksOffense,
                    PerksFlex = group.Key.PerksFlex,
                    PerksDefense = group.Key.PerksDefense,
                    SummonerSpell1Id = group.Key.SummonerSpell1Id,
                    SummonerSpell2Id = group.Key.SummonerSpell2Id,
                    SkillOrderKey = group.Key.SkillOrderKey,
                    StarterItems = group.First().StarterItems,
                    StarterItemsKey = group.Key.StarterItemsKey,
                    BootsItemId = group.Key.BootsItemId,
                    BuildItem0 = group.Key.BuildItem0,
                    BuildItem1 = group.Key.BuildItem1,
                    BuildItem2 = group.Key.BuildItem2,
                    BuildItem3 = group.Key.BuildItem3,
                    BuildItem4 = group.Key.BuildItem4,
                    BuildItem5 = group.Key.BuildItem5,
                    BuildItem6 = group.Key.BuildItem6,
                    Games = group.Count(),
                    Wins = group.Count(entry => entry.Win),
                    LastGameStartTimeUtc = group.Max(entry => entry.GameStartTimeUtc),
                    AggregatedAtUtc = aggregatedAtUtc
                })
                .ToList()
        };
    }

    private async Task<List<ExpandedSourceRow>> ExpandSourceRowsAsync(
        IReadOnlyCollection<AggregateSourceRow> sourceRows,
        CancellationToken ct)
    {
        var expandedRows = new List<ExpandedSourceRow>(sourceRows.Count);

        foreach (var row in sourceRows)
        {
            var itemMetadata = await itemMetadataProvider.GetItemsAsync(row.GameVersion, ct);
            var starterAnalysis = ChampionPatternNormalization.AnalyzeStarterItems(row.ItemEvents, itemMetadata);

            var (spell1Id, spell2Id) = ChampionPatternNormalization.NormalizeSummonerPair(row.Summoner1Id, row.Summoner2Id);
            var skillOrderKey = ChampionPatternNormalization.BuildSkillOrderKey(row.SkillEvents);
            var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
                row.ItemEvents,
                [row.Item0, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item6],
                starterAnalysis.Items,
                itemMetadata);
            var bootsItemId = ChampionPatternNormalization.BuildCorrelatedBootsItem(
                row.ItemEvents,
                [row.Item0, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item6],
                starterAnalysis.Items,
                itemMetadata);
            var slots = PadBuildItems(buildItems);

            expandedRows.Add(new ExpandedSourceRow(
                row.RiotAccountId,
                row.ChampionId,
                row.GameVersion,
                row.PlatformId,
                row.QueueId,
                row.Position!,
                row.PrimaryStyleId,
                row.SubStyleId,
                row.PerksOffense,
                row.PerksFlex,
                row.PerksDefense,
                spell1Id,
                spell2Id,
                skillOrderKey,
                starterAnalysis.Items,
                bootsItemId,
                slots[0],
                slots[1],
                slots[2],
                slots[3],
                slots[4],
                slots[5],
                slots[6],
                row.Win,
                row.GameStartTimeUtc));
        }

        return expandedRows;
    }

    private static int[] PadBuildItems(IReadOnlyList<int> normalizedItems)
    {
        var padded = new int[7];
        for (var i = 0; i < normalizedItems.Count && i < padded.Length; i++)
        {
            padded[i] = normalizedItems[i];
        }

        return padded;
    }
}
