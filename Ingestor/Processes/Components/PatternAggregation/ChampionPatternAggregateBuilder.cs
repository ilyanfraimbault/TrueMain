using Core.Lol.Spells;
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

        var legacyAggregates = BuildLegacyAggregates(expandedRows, aggregatedAtUtc);
        var scopes = BuildScopes(expandedRows, aggregatedAtUtc);

        return new ChampionPatternAggregationResult
        {
            SourceRowCount = sourceRows.Count,
            AggregateRows = legacyAggregates,
            Scopes = scopes
        };
    }

    private static List<ChampionPatternAggregate> BuildLegacyAggregates(
        IReadOnlyList<ExpandedSourceRow> expandedRows,
        DateTime aggregatedAtUtc)
        => expandedRows
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
            .ToList();

    private static List<ChampionAggregateScope> BuildScopes(
        IReadOnlyList<ExpandedSourceRow> expandedRows,
        DateTime aggregatedAtUtc)
        => expandedRows
            .GroupBy(row => new AggregateScopeKeyWithAccount(
                row.RiotAccountId,
                row.ChampionId,
                row.GameVersion,
                row.PlatformId,
                row.QueueId,
                row.Position))
            .OrderBy(group => group.Key.RiotAccountId)
            .ThenBy(group => group.Key.ChampionId)
            .ThenBy(group => group.Key.GameVersion)
            .ThenBy(group => group.Key.PlatformId)
            .ThenBy(group => group.Key.Position)
            .Select(group => BuildScope(group, aggregatedAtUtc))
            .ToList();

    private static ChampionAggregateScope BuildScope(
        IGrouping<AggregateScopeKeyWithAccount, ExpandedSourceRow> group,
        DateTime aggregatedAtUtc)
    {
        var rows = group.ToList();

        return new ChampionAggregateScope
        {
            Id = Guid.NewGuid(),
            RiotAccountId = group.Key.RiotAccountId,
            ChampionId = group.Key.ChampionId,
            GameVersion = group.Key.GameVersion,
            PlatformId = group.Key.PlatformId,
            QueueId = group.Key.QueueId,
            Position = group.Key.Position,
            Games = rows.Count,
            Wins = rows.Count(row => row.Win),
            LastGameStartTimeUtc = rows.Max(row => row.GameStartTimeUtc),
            AggregatedAtUtc = aggregatedAtUtc,
            SpellPairs = rows
                .GroupBy(row => (row.SummonerSpell1Id, row.SummonerSpell2Id))
                .Select(dim => new ChampionAggregateSpellPair
                {
                    Id = Guid.NewGuid(),
                    Spell1Id = dim.Key.SummonerSpell1Id,
                    Spell2Id = dim.Key.SummonerSpell2Id,
                    Games = dim.Count(),
                    Wins = dim.Count(row => row.Win)
                })
                .ToList(),
            SkillOrders = rows
                .GroupBy(row => row.SkillOrderKey)
                .Select(dim => new ChampionAggregateSkillOrder
                {
                    Id = Guid.NewGuid(),
                    SkillOrderKey = dim.Key,
                    Games = dim.Count(),
                    Wins = dim.Count(row => row.Win)
                })
                .ToList(),
            StarterItems = rows
                .GroupBy(row => row.StarterItemsKey)
                .Select(dim => new ChampionAggregateStarterItems
                {
                    Id = Guid.NewGuid(),
                    StarterItemsKey = dim.Key,
                    StarterItems = dim.First().StarterItems,
                    Games = dim.Count(),
                    Wins = dim.Count(row => row.Win)
                })
                .ToList(),
            Builds = rows
                .GroupBy(row => (
                    row.BootsItemId,
                    row.BuildItem0, row.BuildItem1, row.BuildItem2, row.BuildItem3,
                    row.BuildItem4, row.BuildItem5, row.BuildItem6))
                .Select(dim => new ChampionAggregateBuild
                {
                    Id = Guid.NewGuid(),
                    BootsItemId = dim.Key.BootsItemId,
                    BuildItem0 = dim.Key.BuildItem0,
                    BuildItem1 = dim.Key.BuildItem1,
                    BuildItem2 = dim.Key.BuildItem2,
                    BuildItem3 = dim.Key.BuildItem3,
                    BuildItem4 = dim.Key.BuildItem4,
                    BuildItem5 = dim.Key.BuildItem5,
                    BuildItem6 = dim.Key.BuildItem6,
                    Games = dim.Count(),
                    Wins = dim.Count(row => row.Win)
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
            var starterAnalysis = StarterItemAnalyzer.Analyze(row.ItemEvents, itemMetadata);

            var (spell1Id, spell2Id) = new SummonerSpellPair(row.Summoner1Id, row.Summoner2Id).Canonical();
            var skillOrderKey = SkillOrderBuilder.Build(row.SkillEvents);
            int[] finalItems = [row.Item0, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item6];
            var buildItems = FinalBuildResolver.Resolve(row.ItemEvents, finalItems, starterAnalysis.Items, itemMetadata);
            var bootsItemId = BootsResolver.Resolve(row.ItemEvents, finalItems, starterAnalysis.Items, itemMetadata);
            var slots = PadBuildItems(buildItems);

            expandedRows.Add(new ExpandedSourceRow(
                row.RiotAccountId,
                row.ChampionId,
                row.GameVersion,
                row.PlatformId,
                row.QueueId,
                row.Position ?? string.Empty,
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

internal sealed record AggregateScopeKeyWithAccount(
    Guid RiotAccountId,
    int ChampionId,
    string GameVersion,
    string PlatformId,
    int QueueId,
    string Position);
