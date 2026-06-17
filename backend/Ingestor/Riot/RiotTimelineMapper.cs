using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

/// <summary>
/// Maps the raw Riot match timeline payload into the internal <see cref="MatchTimelineDto"/>.
/// Captures per-frame participant state (position, gold, CS, jungle, damage to champions) and
/// event positions / kill participants — the foundation for timeline-derived analytics
/// (per-interval leads, jungle pathing, roam). See issue #538.
/// </summary>
internal static class RiotTimelineMapper
{
    public static MatchTimelineDto Map(RiotTimelineDto timeline)
    {
        var events = new List<MatchTimelineEventDto>();
        var frames = new List<MatchTimelineFrameDto>(timeline.Info.Frames.Count);

        foreach (var frame in timeline.Info.Frames)
        {
            foreach (var evt in frame.Events)
            {
                events.Add(MapEvent(evt));
            }

            frames.Add(new MatchTimelineFrameDto
            {
                TimestampMs = ToTimestamp(frame.Timestamp),
                ParticipantFrames = frame.ParticipantFrames.Values
                    .Select(MapParticipantFrame)
                    .OrderBy(participantFrame => participantFrame.ParticipantId)
                    .ToList()
            });
        }

        return new MatchTimelineDto { Events = events, Frames = frames };
    }

    private static MatchTimelineEventDto MapEvent(RiotTimelineEventDto evt)
        => new()
        {
            // Kill / ward / objective events carry killerId/victimId instead of participantId;
            // they map to 0 here and are filtered out by participant-scoped consumers.
            ParticipantId = evt.ParticipantId ?? 0,
            TimestampMs = ToTimestamp(evt.Timestamp),
            Type = evt.Type,
            ItemId = evt.ItemId,
            BeforeId = evt.BeforeId,
            AfterId = evt.AfterId,
            SkillSlot = evt.SkillSlot,
            LevelUpType = evt.LevelUpType,
            KillerId = evt.KillerId,
            VictimId = evt.VictimId,
            AssistingParticipantIds = evt.AssistingParticipantIds ?? [],
            PositionX = evt.Position?.X,
            PositionY = evt.Position?.Y
        };

    private static MatchParticipantFrameDto MapParticipantFrame(RiotTimelineParticipantFrameDto frame)
        => new()
        {
            ParticipantId = frame.ParticipantId,
            X = frame.Position?.X ?? 0,
            Y = frame.Position?.Y ?? 0,
            CurrentGold = frame.CurrentGold,
            TotalGold = frame.TotalGold,
            Level = frame.Level,
            Xp = frame.Xp,
            MinionsKilled = frame.MinionsKilled,
            JungleMinionsKilled = frame.JungleMinionsKilled,
            TotalDamageToChampions = frame.DamageStats?.TotalDamageDoneToChampions ?? 0
        };

    private static int ToTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp > int.MaxValue ? int.MaxValue : (int)timestamp;
    }
}
