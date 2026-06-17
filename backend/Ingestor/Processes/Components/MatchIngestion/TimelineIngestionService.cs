using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class TimelineIngestionService(IRiotMatchClient riotMatchClient) : ITimelineIngestionService
{
    /// <summary>
    /// Skill events past level 11 add no information for our pattern aggregation
    /// (SkillOrderBuilder only needs to see each basic skill reach rank 2). Cap
    /// what we persist to keep MatchParticipant rows small — see DB optimisation
    /// backlog: SkillEvents tronquer à 11.
    /// </summary>
    internal const int MaxSkillEventsPerParticipant = 11;

    public async Task<int> IngestTimelinesAsync(
        IDataSession session,
        RegionalRoute region,
        IReadOnlyCollection<string> allMatchIds,
        IReadOnlyCollection<string> newMatchIds,
        int saveBatchSize,
        CancellationToken ct)
    {
        var pendingMatchIds = await session.Matches.GetTimelinePendingMatchIdsAsync(allMatchIds, ct);
        var timelineTargetIds = newMatchIds
            .Union(pendingMatchIds, StringComparer.Ordinal)
            .ToList();

        var timelineUpdated = 0;
        var batchSize = Math.Max(1, saveBatchSize);

        for (var i = 0; i < timelineTargetIds.Count; i += batchSize)
        {
            var batch = timelineTargetIds.Skip(i).Take(batchSize).ToList();
            foreach (var matchId in batch)
            {
                var timelineDto = await riotMatchClient.GetTimelineAsync(matchId, region, ct);
                var applied = await ApplyTimelineAsync(session, matchId, timelineDto, ct);
                if (!applied)
                {
                    continue;
                }

                await session.Matches.SetTimelineIngestedAsync(matchId, true, ct);
                timelineUpdated++;
            }

            await session.SaveChangesAsync(ct);
        }

        return timelineUpdated;
    }

    private static async Task<bool> ApplyTimelineAsync(
        IDataSession session,
        string matchId,
        MatchTimelineDto timeline,
        CancellationToken ct)
    {
        var participants = await session.MatchParticipants.GetByMatchIdAsync(matchId, ct);
        if (participants.Count == 0)
        {
            return false;
        }

        var itemEventsByParticipant = new Dictionary<int, List<ItemEvent>>();
        var skillEventsByParticipant = new Dictionary<int, List<SkillEvent>>();

        foreach (var timelineEvent in timeline.Events)
        {
            if (timelineEvent.ParticipantId <= 0)
            {
                continue;
            }

            AddItemEventIfApplicable(itemEventsByParticipant, timelineEvent);
            AddSkillEventIfApplicable(skillEventsByParticipant, timelineEvent);
        }

        foreach (var participant in participants)
        {
            participant.ItemEvents = itemEventsByParticipant.TryGetValue(participant.ParticipantId, out var itemEvents)
                ? itemEvents
                : [];

            participant.SkillEvents = skillEventsByParticipant.TryGetValue(participant.ParticipantId, out var skillEvents)
                ? TruncateSkillEvents(skillEvents)
                : [];
        }

        // Replace any existing per-interval snapshots so re-ingesting a timeline is
        // idempotent (delete is immediate to avoid the unique-index race; the fresh
        // inserts flush with the participant updates on the caller's SaveChanges).
        // The delete commits before that SaveChanges, so if the save fails the
        // TimelineIngested flag is rolled back with it: the match stays pending and is
        // re-ingested (delete + reinsert again) — a transient empty window, not lost data.
        await session.MatchParticipantTimelineSnapshots.DeleteByMatchIdAsync(matchId, ct);
        session.MatchParticipantTimelineSnapshots.AddRange(TimelineSnapshotBuilder.Build(matchId, timeline));

        return true;
    }

    private static void AddItemEventIfApplicable(
        IDictionary<int, List<ItemEvent>> itemEventsByParticipant,
        MatchTimelineEventDto timelineEvent)
    {
        if (!timelineEvent.Type.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase) || !timelineEvent.ItemId.HasValue)
        {
            return;
        }

        if (!itemEventsByParticipant.TryGetValue(timelineEvent.ParticipantId, out var itemEvents))
        {
            itemEvents = [];
            itemEventsByParticipant[timelineEvent.ParticipantId] = itemEvents;
        }

        itemEvents.Add(new ItemEvent
        {
            TimestampMs = timelineEvent.TimestampMs,
            EventType = timelineEvent.Type,
            ItemId = timelineEvent.ItemId.Value,
            BeforeId = timelineEvent.BeforeId,
            AfterId = timelineEvent.AfterId
        });
    }

    internal static List<SkillEvent> TruncateSkillEvents(List<SkillEvent> skillEvents)
    {
        if (skillEvents.Count <= MaxSkillEventsPerParticipant)
        {
            return skillEvents;
        }

        return skillEvents
            .OrderBy(skillEvent => skillEvent.TimestampMs)
            .Take(MaxSkillEventsPerParticipant)
            .ToList();
    }

    private static void AddSkillEventIfApplicable(
        IDictionary<int, List<SkillEvent>> skillEventsByParticipant,
        MatchTimelineEventDto timelineEvent)
    {
        if (!string.Equals(timelineEvent.Type, "SKILL_LEVEL_UP", StringComparison.OrdinalIgnoreCase)
            || !timelineEvent.SkillSlot.HasValue)
        {
            return;
        }

        if (!skillEventsByParticipant.TryGetValue(timelineEvent.ParticipantId, out var skillEvents))
        {
            skillEvents = [];
            skillEventsByParticipant[timelineEvent.ParticipantId] = skillEvents;
        }

        skillEvents.Add(new SkillEvent
        {
            TimestampMs = timelineEvent.TimestampMs,
            SkillSlot = timelineEvent.SkillSlot.Value,
            LevelUpType = timelineEvent.LevelUpType ?? string.Empty
        });
    }
}
