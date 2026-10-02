using Core.Lol.Identifiers;
using Core.Lol.Patches;
using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class TimelineIngestionService(
    IRiotMatchClient riotMatchClient,
    IOptions<MainAnalysisOptions> analysisOptions,
    TimeProvider timeProvider) : ITimelineIngestionService
{
    private readonly int _analysisQueueId = (int)analysisOptions.Value.QueueId;

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

    private async Task<bool> ApplyTimelineAsync(
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

        // Fold the timeline into the powerspike/lead aggregates instead of storing a
        // per-minute snapshot grid. Only the configured analysis queue contributes
        // (its population is what the champion reads slice). The claim flips
        // TimelineAggregated false→true atomically, so the same match ingested from a
        // second tracked account cannot double-count into the add-only accumulators;
        // it commits or rolls back with MatchIngestionProcess's transaction.
        await AccumulateTimelineAsync(session, matchId, participants, timeline, ct);

        // Bounded early-game kill-participation positions for the roam metric (#536),
        // replaced idempotently: the delete clears the slots, the fresh inserts flush
        // with the participant updates on the caller's SaveChanges.
        await session.MatchParticipantKillPositions.DeleteByMatchIdAsync(matchId, ct);
        session.MatchParticipantKillPositions.AddRange(KillPositionBuilder.Build(matchId, timeline));

        return true;
    }

    private async Task AccumulateTimelineAsync(
        IDataSession session,
        string matchId,
        IReadOnlyList<MatchParticipant> participants,
        MatchTimelineDto timeline,
        CancellationToken ct)
    {
        var info = await session.Matches.GetAggregationInfoAsync(matchId, ct);
        if (info is null || info.QueueId != _analysisQueueId)
        {
            return;
        }

        var patch = PatchVersion.Normalize(info.GameVersion);
        if (string.IsNullOrEmpty(patch))
        {
            return;
        }

        // Claim last: only the caller that flips the flag folds the contribution, so
        // the same match ingested from two accounts is counted exactly once.
        if (!await session.Matches.TryClaimTimelineAggregationAsync(matchId, ct))
        {
            return;
        }

        var contribution = TimelineAggregationBuilder.Build(info.QueueId, patch, participants, timeline);
        await session.TimelineAggregates.ApplyAsync(contribution, timeProvider.GetUtcNow().UtcDateTime, ct);
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
