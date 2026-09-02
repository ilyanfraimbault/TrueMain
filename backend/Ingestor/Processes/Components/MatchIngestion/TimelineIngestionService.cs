using System.Text.Json;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class TimelineIngestionService(
    IRiotMatchClient riotMatchClient,
    ILogger<TimelineIngestionService> logger) : ITimelineIngestionService
{
    /// <summary>
    /// Skill events past level 11 add no information for our pattern aggregation
    /// (SkillOrderBuilder only needs to see each basic skill reach rank 2). Cap
    /// what we persist to keep the MatchParticipant jsonb rows small.
    /// </summary>
    internal const int MaxSkillEventsPerParticipant = 11;

    /// <summary>
    /// How many timelines in a row may fail to download before we stop treating the
    /// failures as isolated bad payloads and let the exception abort the account.
    /// One truncated body is Riot flakiness; five back to back is an outage, and
    /// swallowing those would report a healthy run that ingested nothing.
    /// </summary>
    internal const int MaxConsecutiveTimelineFailures = 5;

    public async Task<TimelineIngestionPlan> PrepareAsync(
        IDataSession session,
        RegionalRoute region,
        IReadOnlyCollection<string> allMatchIds,
        IReadOnlyCollection<string> newMatchIds,
        CancellationToken ct)
    {
        var pendingMatchIds = await session.Matches.GetTimelinePendingMatchIdsAsync(allMatchIds, ct);
        var timelineTargetIds = newMatchIds
            .Union(pendingMatchIds, StringComparer.Ordinal)
            .ToList();

        var timelines = new List<FetchedTimeline>(timelineTargetIds.Count);
        var consecutiveFailures = 0;

        foreach (var matchId in timelineTargetIds)
        {
            MatchTimelineDto timelineDto;
            try
            {
                timelineDto = await riotMatchClient.GetTimelineAsync(matchId, region, ct);
            }
            // Riot occasionally cuts a timeline body short: the response is a 200 whose
            // payload dies mid-stream. The resilience handler cannot retry it — it decides
            // on the headers, and since #253 the body is deserialized off the still-flowing
            // stream, outside the pipeline. Isolate the bad match instead of letting it
            // abort the account: leaving TimelineIngested false hands it to the
            // pending-timeline path, which re-fetches it on a later run.
            catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException
                                       && !ct.IsCancellationRequested)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= MaxConsecutiveTimelineFailures)
                {
                    throw;
                }

                logger.LogWarning(
                    ex,
                    "Timeline download failed for {MatchId}; leaving it pending for a later run.",
                    matchId);
                continue;
            }

            consecutiveFailures = 0;
            timelines.Add(new FetchedTimeline(matchId, timelineDto));
        }

        return new TimelineIngestionPlan(timelines);
    }

    public async Task<int> WriteAsync(
        IDataSession session,
        TimelineIngestionPlan plan,
        int saveBatchSize,
        CancellationToken ct)
    {
        var timelineUpdated = 0;
        var batchSize = Math.Max(1, saveBatchSize);

        for (var i = 0; i < plan.Timelines.Count; i += batchSize)
        {
            var batch = plan.Timelines.Skip(i).Take(batchSize).ToList();
            var appliedMatchIds = new List<string>(batch.Count);

            foreach (var (matchId, timelineDto) in batch)
            {
                if (await ApplyTimelineAsync(session, matchId, timelineDto, ct))
                {
                    appliedMatchIds.Add(matchId);
                }
            }

            if (appliedMatchIds.Count == 0)
            {
                continue;
            }

            // Replace any existing per-interval snapshots so re-ingesting a timeline is
            // idempotent: the deletes run first as SQL (clearing the unique-index slots),
            // then the fresh inserts staged above flush with the participant updates on the
            // SaveChanges below. Set-based over the whole batch rather than per match
            // (#1229): the batch is already assembled, so three statements do the work three
            // per match used to. MatchIngestionProcess wraps this in a transaction, so the
            // deletes and the reinserts commit together (or roll back together on failure) —
            // no window where a match is left without snapshots.
            await session.MatchParticipantTimelineSnapshots.DeleteByMatchIdsAsync(appliedMatchIds, ct);

            // Bounded early-game kill-participation positions for the roam metric (#536),
            // replaced idempotently the same way.
            await session.MatchParticipantKillPositions.DeleteByMatchIdsAsync(appliedMatchIds, ct);

            await session.Matches.SetTimelineIngestedAsync(appliedMatchIds, true, ct);
            await session.SaveChangesAsync(ct);
            timelineUpdated += appliedMatchIds.Count;
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

        // Staged, not flushed: WriteAsync issues the batch's two deletes before the
        // SaveChanges that inserts these, so the unique-index slots are free by then.
        session.MatchParticipantTimelineSnapshots.AddRange(TimelineSnapshotBuilder.Build(matchId, timeline));
        session.MatchParticipantKillPositions.AddRange(KillPositionBuilder.Build(matchId, timeline));

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
