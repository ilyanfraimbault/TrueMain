using Core;
using Data;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor;

public class Worker(
    ILogger<Worker> logger,
    IRiotMatchClient riotMatchClient,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<RiotOptions> riotOptions,
    IOptions<SeedOptions> seedOptions,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = riotOptions.Value;
        var seeds = seedOptions.Value.MatchIds;

        if (seeds.Count == 0)
        {
            logger.LogWarning("No seed match IDs configured (Seed:MatchIds).");
            applicationLifetime.StopApplication();
            return;
        }

        foreach (var matchId in seeds)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await IngestMatchAsync(matchId, options.RegionalRoute, stoppingToken);
        }

        applicationLifetime.StopApplication();
    }

    private async Task IngestMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
    {
        logger.LogInformation("Ingesting match {MatchId} ({Region}).", matchId, region);

        var match = await riotMatchClient.GetMatchAsync(matchId, region, ct);
        var timeline = await riotMatchClient.GetTimelineAsync(matchId, region, ct);

        var effectiveMatchId = string.IsNullOrWhiteSpace(match.Metadata.MatchId) ? matchId : match.Metadata.MatchId;
        var participants = MapParticipants(match, effectiveMatchId);
        var perkSelections = MapPerkSelections(match, effectiveMatchId);

        ApplyTimelineEvents(participants, timeline);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var existingParticipants = await db.MatchParticipants
            .Where(p => p.MatchId == effectiveMatchId)
            .ToListAsync(ct);

        var existingByParticipantId = existingParticipants.ToDictionary(p => p.ParticipantId);

        foreach (var participant in participants)
        {
            if (existingByParticipantId.TryGetValue(participant.ParticipantId, out var existing))
            {
                participant.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(participant);
                existing.ItemEvents = participant.ItemEvents;
                existing.SkillEvents = participant.SkillEvents;
            }
            else
            {
                db.MatchParticipants.Add(participant);
            }
        }

        await db.ParticipantPerkSelections
            .Where(p => p.MatchId == effectiveMatchId)
            .ExecuteDeleteAsync(ct);

        db.ParticipantPerkSelections.AddRange(perkSelections);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Ingestion complete for match {MatchId}.", effectiveMatchId);
    }

    private static List<MatchParticipant> MapParticipants(RiotMatchDto match, string matchId)
    {
        var participants = new List<MatchParticipant>(match.Info.Participants.Count);

        foreach (var p in match.Info.Participants)
        {
            var primaryStyle = p.Perks.Styles.FirstOrDefault(s =>
                string.Equals(s.Description, "primaryStyle", StringComparison.OrdinalIgnoreCase));
            var subStyle = p.Perks.Styles.FirstOrDefault(s =>
                string.Equals(s.Description, "subStyle", StringComparison.OrdinalIgnoreCase));

            participants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = p.ParticipantId,
                Puuid = p.Puuid,
                SummonerName = p.SummonerName,
                SummonerLevel = p.SummonerLevel,
                ChampionId = p.ChampionId,
                TeamId = p.TeamId,
                TeamPosition = p.TeamPosition ?? string.Empty,
                IndividualPosition = p.IndividualPosition ?? string.Empty,
                Lane = p.Lane ?? string.Empty,
                Role = p.Role ?? string.Empty,
                Win = p.Win,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                GoldEarned = p.GoldEarned,
                TotalMinionsKilled = p.TotalMinionsKilled,
                NeutralMinionsKilled = p.NeutralMinionsKilled,
                ChampLevel = p.ChampLevel,
                Item0 = p.Item0,
                Item1 = p.Item1,
                Item2 = p.Item2,
                Item3 = p.Item3,
                Item4 = p.Item4,
                Item5 = p.Item5,
                Item6 = p.Item6,
                TrinketItemId = p.Item6,
                PerksDefense = p.Perks.StatPerks.Defense,
                PerksFlex = p.Perks.StatPerks.Flex,
                PerksOffense = p.Perks.StatPerks.Offense,
                PrimaryStyleId = primaryStyle?.Style ?? 0,
                SubStyleId = subStyle?.Style ?? 0,
                Summoner1Id = p.Summoner1Id,
                Summoner2Id = p.Summoner2Id,
                ItemEvents = new List<ItemEvent>(),
                SkillEvents = new List<SkillEvent>()
            });
        }

        return participants;
    }

    private static List<ParticipantPerkSelection> MapPerkSelections(RiotMatchDto match, string matchId)
    {
        var selections = new List<ParticipantPerkSelection>();

        foreach (var participant in match.Info.Participants)
        {
            foreach (var style in participant.Perks.Styles)
            {
                for (var i = 0; i < style.Selections.Count; i++)
                {
                    var selection = style.Selections[i];
                    selections.Add(new ParticipantPerkSelection
                    {
                        MatchId = matchId,
                        ParticipantId = participant.ParticipantId,
                        StyleId = style.Style,
                        StyleDescription = style.Description ?? string.Empty,
                        SelectionIndex = i,
                        PerkId = selection.Perk
                    });
                }
            }
        }

        return selections;
    }

    private static void ApplyTimelineEvents(List<MatchParticipant> participants, RiotTimelineDto timeline)
    {
        var itemEventsByParticipant = new Dictionary<int, List<ItemEvent>>();
        var skillEventsByParticipant = new Dictionary<int, List<SkillEvent>>();

        foreach (var frame in timeline.Info.Frames)
        {
            foreach (var evt in frame.Events)
            {
                if (evt.ParticipantId is null)
                {
                    continue;
                }

                var participantId = evt.ParticipantId.Value;

                if (evt.Type.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase) && evt.ItemId.HasValue)
                {
                    if (!itemEventsByParticipant.TryGetValue(participantId, out var itemEvents))
                    {
                        itemEvents = new List<ItemEvent>();
                        itemEventsByParticipant[participantId] = itemEvents;
                    }

                    itemEvents.Add(new ItemEvent
                    {
                        TimestampMs = ToTimestamp(evt.Timestamp),
                        EventType = evt.Type,
                        ItemId = evt.ItemId.Value,
                        BeforeId = evt.BeforeId,
                        AfterId = evt.AfterId
                    });
                }

                if (string.Equals(evt.Type, "SKILL_LEVEL_UP", StringComparison.OrdinalIgnoreCase) && evt.SkillSlot.HasValue)
                {
                    if (!skillEventsByParticipant.TryGetValue(participantId, out var skillEvents))
                    {
                        skillEvents = new List<SkillEvent>();
                        skillEventsByParticipant[participantId] = skillEvents;
                    }

                    skillEvents.Add(new SkillEvent
                    {
                        TimestampMs = ToTimestamp(evt.Timestamp),
                        SkillSlot = evt.SkillSlot.Value,
                        LevelUpType = evt.LevelUpType ?? string.Empty
                    });
                }
            }
        }

        foreach (var participant in participants)
        {
            if (itemEventsByParticipant.TryGetValue(participant.ParticipantId, out var itemEvents))
            {
                participant.ItemEvents = itemEvents;
            }

            if (skillEventsByParticipant.TryGetValue(participant.ParticipantId, out var skillEvents))
            {
                participant.SkillEvents = skillEvents;
            }
        }
    }

    private static int ToTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp > int.MaxValue ? int.MaxValue : (int)timestamp;
    }
}
