using Core.Options;
using Data;
using Data.Entities;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Ingestor.Processes;

public sealed class ChampionPatternAggregationProcess(
    ILogger<ChampionPatternAggregationProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IProcessRunRecorder runRecorder,
    IOptions<MainAnalysisOptions> analysisOptions,
    IItemMetadataProvider itemMetadataProvider)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var startedAtUtc = DateTime.UtcNow;
        var nowUtc = DateTime.UtcNow;
        var queueId = analysisOptions.Value.QueueId;

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            var existingAggregateScopes = await db.ChampionPatternAggregates
                .AsNoTracking()
                .Where(aggregate => aggregate.QueueId == queueId)
                .Select(aggregate => new AggregateScopeKey(
                    aggregate.ChampionId,
                    aggregate.GameVersion,
                    aggregate.PlatformId,
                    aggregate.QueueId))
                .Distinct()
                .ToListAsync(ct);

            var sourceRows = await (
                from participant in db.MatchParticipants.AsNoTracking()
                join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
                join stat in db.MainChampionStats.AsNoTracking()
                    on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                    equals new { stat.PlatformId, stat.Puuid, stat.ChampionId }
                where stat.IsMain
                    && participant.RiotAccountId != null
                    && match.QueueId == queueId
                    && match.TimelineIngested
                select new AggregateSourceRow
                {
                    MatchId = match.Id,
                    ChampionId = participant.ChampionId,
                    GameVersion = ChampionPatternNormalization.NormalizePatchVersion(match.GameVersion),
                    PlatformId = match.PlatformId,
                    QueueId = match.QueueId,
                    GameStartTimeUtc = match.GameStartTimeUtc,
                    RiotAccountId = participant.RiotAccountId!.Value,
                    Win = participant.Win,
                    Position = ChampionPatternNormalization.NormalizeTeamPosition(participant.TeamPosition),
                    Summoner1Id = participant.Summoner1Id,
                    Summoner2Id = participant.Summoner2Id,
                    PrimaryStyleId = participant.PrimaryStyleId,
                    SubStyleId = participant.SubStyleId,
                    PerksOffense = participant.PerksOffense,
                    PerksFlex = participant.PerksFlex,
                    PerksDefense = participant.PerksDefense,
                    ItemEvents = participant.ItemEvents,
                    SkillEvents = participant.SkillEvents,
                    Item0 = participant.Item0,
                    Item1 = participant.Item1,
                    Item2 = participant.Item2,
                    Item3 = participant.Item3,
                    Item4 = participant.Item4,
                    Item5 = participant.Item5
                })
                .ToListAsync(ct);

            sourceRows = sourceRows
            .Where(AggregateSourceRowHasCompleteCorrelatedTimeline)
            .ToList();

            var rebuildScopes = sourceRows
                .Select(row => new AggregateScopeKey(row.ChampionId, row.GameVersion, row.PlatformId, row.QueueId))
                .Distinct()
                .ToList();

            var cleanupScopes = existingAggregateScopes;

            if (sourceRows.Count == 0 && cleanupScopes.Count == 0)
            {
                logger.LogInformation("No specialist-backed source rows available for champion pattern aggregation.");
                await RecordNoOpAsync(startedAtUtc, "No specialist-backed source rows available for champion pattern aggregation.", ct);
                return;
            }

            var aggregateRows = await BuildAggregateRowsAsync(sourceRows, nowUtc, ct);
            var cleanupScopeSet = cleanupScopes.ToHashSet();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (cleanupScopeSet.Count > 0)
            {
                var cleanupChampionIds = cleanupScopeSet.Select(scope => scope.ChampionId).Distinct().ToList();
                var cleanupGameVersions = cleanupScopeSet.Select(scope => scope.GameVersion).Distinct().ToList();
                var cleanupPlatformIds = cleanupScopeSet.Select(scope => scope.PlatformId).Distinct().ToList();
                var cleanupQueueIds = cleanupScopeSet.Select(scope => scope.QueueId).Distinct().ToList();

                var aggregatesToDelete = await db.ChampionPatternAggregates
                    .Where(aggregate =>
                        cleanupChampionIds.Contains(aggregate.ChampionId)
                        && cleanupGameVersions.Contains(aggregate.GameVersion)
                        && cleanupPlatformIds.Contains(aggregate.PlatformId)
                        && cleanupQueueIds.Contains(aggregate.QueueId))
                    .ToListAsync(ct);

                db.ChampionPatternAggregates.RemoveRange(
                    aggregatesToDelete.Where(aggregate =>
                        cleanupScopeSet.Contains(new AggregateScopeKey(
                            aggregate.ChampionId,
                            aggregate.GameVersion,
                            aggregate.PlatformId,
                            aggregate.QueueId))));
            }
            await db.SaveChangesAsync(ct);

            db.ChampionPatternAggregates.AddRange(aggregateRows);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation(
                "Champion pattern aggregation summary: sourceRows={SourceRows}, aggregateRows={AggregateRows}.",
                sourceRows.Count,
                aggregateRows.Count);

            await runRecorder.RecordAsync(
                "ChampionPatternAggregation",
                startedAtUtc,
                DateTime.UtcNow,
                ProcessRunStatus.Success,
                new
                {
                    sourceRows = sourceRows.Count,
                    aggregateRows = aggregateRows.Count,
                    gameVersions = aggregateRows.Select(a => a.GameVersion).Distinct().Count(),
                    champions = aggregateRows.Select(a => a.ChampionId).Distinct().Count()
                },
                null,
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordAsync(
                "ChampionPatternAggregation",
                startedAtUtc,
                DateTime.UtcNow,
                ProcessRunStatus.Failed,
                null,
                ex.Message,
                ct);
            throw;
        }
    }

    private async Task<List<ChampionPatternAggregate>> BuildAggregateRowsAsync(
        IReadOnlyCollection<AggregateSourceRow> sourceRows,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var expandedRows = new List<ExpandedSourceRow>(sourceRows.Count);
        var loggedEmptyStarterSamples = 0;

        foreach (var row in sourceRows)
        {
            var itemMetadata = await itemMetadataProvider.GetItemsAsync(row.GameVersion, ct);
            var (spell1Id, spell2Id) = ChampionPatternNormalization.NormalizeSummonerPair(row.Summoner1Id, row.Summoner2Id);
            var starterAnalysis = ChampionPatternNormalization.AnalyzeStarterItems(row.ItemEvents, itemMetadata);
            var starterItems = starterAnalysis.Items;
            var skillOrderKey = ChampionPatternNormalization.BuildSkillOrderKey(row.SkillEvents);
            var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
                row.ItemEvents,
                [row.Item0, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5],
                itemMetadata);
            var slots = PadBuildItems(buildItems);

            if (starterItems.Count == 0 && loggedEmptyStarterSamples < 3)
            {
                loggedEmptyStarterSamples++;
                logger.LogInformation(
                    "Starter detection sample {SampleIndex}: matchId={MatchId}, riotAccountId={RiotAccountId}, championId={ChampionId}, patch={Patch}, position={Position}, reason={Reason}, totalCost={TotalCost}, earlyEvents={EarlyEvents}",
                    loggedEmptyStarterSamples,
                    row.MatchId,
                    row.RiotAccountId,
                    row.ChampionId,
                    row.GameVersion,
                    row.Position ?? "UNKNOWN",
                    starterAnalysis.Reason,
                    starterAnalysis.TotalCost,
                    JsonSerializer.Serialize(starterAnalysis.EarlyEvents.Select(static itemEvent => new
                    {
                        itemEvent.TimestampMs,
                        itemEvent.EventType,
                        itemEvent.ItemId,
                        itemEvent.BeforeId,
                        itemEvent.AfterId
                    })));
            }

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
                starterItems,
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

        return expandedRows
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

    private async Task RecordNoOpAsync(DateTime startedAtUtc, string reason, CancellationToken ct)
    {
        await runRecorder.RecordAsync(
            "ChampionPatternAggregation",
            startedAtUtc,
            DateTime.UtcNow,
            ProcessRunStatus.Success,
            new { reason, aggregateRows = 0 },
            null,
            ct);
    }

    private static bool AggregateSourceRowHasCompleteCorrelatedTimeline(AggregateSourceRow row)
    {
        var purchaseCount = row.ItemEvents.Count(itemEvent =>
            itemEvent.ItemId > 0
            && itemEvent.EventType.Equals("ITEM_PURCHASED", StringComparison.OrdinalIgnoreCase));
        var normalSkillLevelUps = row.SkillEvents.Count(skillEvent =>
            skillEvent.LevelUpType.Equals("NORMAL", StringComparison.OrdinalIgnoreCase));

        return purchaseCount > 0
            && normalSkillLevelUps >= 3
            && !string.IsNullOrWhiteSpace(row.Position);
    }

    private sealed class AggregateSourceRow
    {
        public string MatchId { get; init; } = string.Empty;
        public int ChampionId { get; init; }
        public string GameVersion { get; init; } = string.Empty;
        public string PlatformId { get; init; } = string.Empty;
        public int QueueId { get; init; }
        public DateTime GameStartTimeUtc { get; init; }
        public Guid RiotAccountId { get; init; }
        public bool Win { get; init; }
        public string? Position { get; init; }
        public int Summoner1Id { get; init; }
        public int Summoner2Id { get; init; }
        public int PrimaryStyleId { get; init; }
        public int SubStyleId { get; init; }
        public int PerksOffense { get; init; }
        public int PerksFlex { get; init; }
        public int PerksDefense { get; init; }
        public List<ItemEvent> ItemEvents { get; init; } = [];
        public List<SkillEvent> SkillEvents { get; init; } = [];
        public int Item0 { get; init; }
        public int Item1 { get; init; }
        public int Item2 { get; init; }
        public int Item3 { get; init; }
        public int Item4 { get; init; }
        public int Item5 { get; init; }
    }

    private sealed record ExpandedSourceRow(
        Guid RiotAccountId,
        int ChampionId,
        string GameVersion,
        string PlatformId,
        int QueueId,
        string Position,
        int PrimaryStyleId,
        int SubStyleId,
        int PerksOffense,
        int PerksFlex,
        int PerksDefense,
        int SummonerSpell1Id,
        int SummonerSpell2Id,
        string SkillOrderKey,
        List<int> StarterItems,
        int BuildItem0,
        int BuildItem1,
        int BuildItem2,
        int BuildItem3,
        int BuildItem4,
        int BuildItem5,
        int BuildItem6,
        bool Win,
        DateTime GameStartTimeUtc)
    {
        public string StarterItemsKey { get; } = string.Join("-", StarterItems);
    }

    private sealed record AggregateKey(
        Guid RiotAccountId,
        int ChampionId,
        string GameVersion,
        string PlatformId,
        int QueueId,
        string Position,
        int PrimaryStyleId,
        int SubStyleId,
        int PerksOffense,
        int PerksFlex,
        int PerksDefense,
        int SummonerSpell1Id,
        int SummonerSpell2Id,
        string SkillOrderKey,
        string StarterItemsKey,
        int BuildItem0,
        int BuildItem1,
        int BuildItem2,
        int BuildItem3,
        int BuildItem4,
        int BuildItem5,
        int BuildItem6);

    private sealed record AggregateScopeKey(
        int ChampionId,
        string GameVersion,
        string PlatformId,
        int QueueId);
}
