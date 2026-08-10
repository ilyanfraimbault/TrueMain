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
    /// what we persist to keep MatchParticipant rows small — see DB optimisation
    /// backlog: SkillEvents tronquer à 11.
    /// </summary>
    internal const int MaxSkillEventsPerParticipant = 11;

    /// <summary>
    /// How many timelines in a row may fail to download before we stop treating the
    /// failures as isolated bad payloads and let the exception abort the account.
    /// One truncated body is Riot flakiness; five back to back is an outage, and
    /// swallowing those would report a healthy run that ingested nothing.
    /// </summary>
    internal const int MaxConsecutiveTimelineFailures = 5;

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
        var consecutiveFailures = 0;

        for (var i = 0; i < timelineTargetIds.Count; i += batchSize)
        {
            var batch = timelineTargetIds.Skip(i).Take(batchSize).ToList();
            foreach (var matchId in batch)
            {
                MatchTimelineDto timelineDto;
                try
                {
                    timelineDto = await riotMatchClient.GetTimelineAsync(matchId, region, ct);
                }
                // Riot occasionally cuts a timeline body short: the response is a
                // 200 whose payload dies mid-stream. The resilience handler cannot
                // retry it — it decides on the headers, and since #253 the body is
                // deserialized off the still-flowing stream, outside the pipeline.
                // Isolate the bad match instead of letting it roll back the whole
                // account's transaction: leaving TimelineIngested false hands it to
                // the pending-timeline path, which re-fetches it on a later run.
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
        // idempotent: the delete runs first as SQL (clearing the unique-index slots),
        // then the fresh inserts flush with the participant updates on the caller's
        // SaveChanges. MatchIngestionProcess wraps this in a transaction, so the delete
        // and the reinserts commit together (or roll back together on failure) — no
        // window where the match is left without snapshots.
        await session.MatchParticipantTimelineSnapshots.DeleteByMatchIdAsync(matchId, ct);
        session.MatchParticipantTimelineSnapshots.AddRange(TimelineSnapshotBuilder.Build(matchId, timeline));

        // Bounded early-game kill-participation positions for the roam metric (#536),
        // replaced idempotently the same way.
        await session.MatchParticipantKillPositions.DeleteByMatchIdAsync(matchId, ct);
        session.MatchParticipantKillPositions.AddRange(KillPositionBuilder.Build(matchId, timeline));

        // Reconstructed jungler first clear (#535) — camp order + per-camp/full-clear
        // timing inferred from the in-memory per-minute frames, replaced idempotently.
        await session.JungleFirstClears.DeleteByMatchIdAsync(matchId, ct);
        session.JungleFirstClears.AddRange(JungleClearBuilder.Build(matchId, timeline));

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
