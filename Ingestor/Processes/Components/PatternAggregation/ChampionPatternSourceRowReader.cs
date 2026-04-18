using Core.Lol.Map;
using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternSourceRowReader(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    private const int MinimumAggregatedGameDurationSeconds = 15 * 60;

    internal async Task<ChampionPatternAggregationInputs> LoadAggregationInputsAsync(
        int queueId,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var existingAggregateScopes = await LoadExistingAggregateScopesAsync(db, queueId, ct);
        var sourceRows = await LoadSourceRowsAsync(db, queueId, ct);

        return new ChampionPatternAggregationInputs
        {
            ExistingAggregateScopes = existingAggregateScopes,
            SourceRows = sourceRows
        };
    }

    private static Task<List<AggregateScopeKey>> LoadExistingAggregateScopesAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        return db.ChampionPatternAggregates
            .AsNoTracking()
            .Where(aggregate => aggregate.QueueId == queueId)
            .Select(aggregate => new AggregateScopeKey(
                aggregate.ChampionId,
                aggregate.GameVersion,
                aggregate.PlatformId,
                aggregate.QueueId))
            .Distinct()
            .ToListAsync(ct);
    }

    private static async Task<List<AggregateSourceRow>> LoadSourceRowsAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
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
                GameVersion = PatchVersion.Normalize(match.GameVersion),
                PlatformId = match.PlatformId,
                QueueId = match.QueueId,
                GameStartTimeUtc = match.GameStartTimeUtc,
                GameDurationSeconds = match.GameDurationSeconds,
                RiotAccountId = participant.RiotAccountId!.Value,
                Win = participant.Win,
                Position = LolPositionExtensions.Parse(participant.TeamPosition).ToRiotString(),
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
                Item5 = participant.Item5,
                Item6 = participant.Item6
            })
            .ToListAsync(ct);

        return sourceRows
            .Where(HasCompleteCorrelatedTimeline)
            .ToList();
    }

    private static bool HasCompleteCorrelatedTimeline(AggregateSourceRow row)
    {
        var purchaseCount = row.ItemEvents.Count(itemEvent =>
            itemEvent.ItemId > 0
            && itemEvent.EventType.Equals("ITEM_PURCHASED", StringComparison.OrdinalIgnoreCase));
        var normalSkillLevelUps = row.SkillEvents.Count(skillEvent =>
            skillEvent.LevelUpType.Equals("NORMAL", StringComparison.OrdinalIgnoreCase));

        return purchaseCount > 0
            && row.GameDurationSeconds >= MinimumAggregatedGameDurationSeconds
            && normalSkillLevelUps >= 3
            && !string.IsNullOrWhiteSpace(row.Position);
    }
}
