using Data.Entities;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Builds per-interval timeline snapshots (issue #525) from a match timeline:
/// one row per participant at each fixed minute mark, with raw values only.
/// The "lead vs lane opponent" is intentionally not computed here — it is a
/// read-time delta against the opposing teamPosition.
/// </summary>
internal static class TimelineSnapshotBuilder
{
    internal static readonly int[] IntervalMinutes = [5, 10, 15, 20, 30];

    // A minute mark is only captured if a frame sits within half a minute of it,
    // so games that ended before a mark simply produce no row for it.
    private const int FrameMatchToleranceMs = 30_000;

    public static List<MatchParticipantTimelineSnapshot> Build(string matchId, MatchTimelineDto timeline)
    {
        var snapshots = new List<MatchParticipantTimelineSnapshot>();
        if (timeline.Frames.Count == 0)
        {
            return snapshots;
        }

        var killTimestamps = new Dictionary<int, List<int>>();
        var wardPlacedTimestamps = new Dictionary<int, List<int>>();
        var wardKilledTimestamps = new Dictionary<int, List<int>>();

        foreach (var timelineEvent in timeline.Events)
        {
            switch (timelineEvent.Type.ToUpperInvariant())
            {
                case "CHAMPION_KILL" when timelineEvent.KillerId is > 0:
                    Record(killTimestamps, timelineEvent.KillerId.Value, timelineEvent.TimestampMs);
                    break;
                case "WARD_PLACED" when timelineEvent.CreatorId is > 0:
                    Record(wardPlacedTimestamps, timelineEvent.CreatorId.Value, timelineEvent.TimestampMs);
                    break;
                case "WARD_KILL" when timelineEvent.KillerId is > 0:
                    Record(wardKilledTimestamps, timelineEvent.KillerId.Value, timelineEvent.TimestampMs);
                    break;
            }
        }

        foreach (var minute in IntervalMinutes)
        {
            var frame = SelectFrame(timeline.Frames, minute * 60_000);
            if (frame is null)
            {
                continue;
            }

            foreach (var participantFrame in frame.ParticipantFrames)
            {
                snapshots.Add(new MatchParticipantTimelineSnapshot
                {
                    MatchId = matchId,
                    ParticipantId = participantFrame.ParticipantId,
                    IntervalMinute = minute,
                    TimestampMs = frame.TimestampMs,
                    TotalGold = participantFrame.TotalGold,
                    MinionsKilled = participantFrame.MinionsKilled,
                    JungleMinionsKilled = participantFrame.JungleMinionsKilled,
                    Level = participantFrame.Level,
                    Xp = participantFrame.Xp,
                    DamageToChampions = participantFrame.TotalDamageToChampions,
                    Kills = CountUpTo(killTimestamps, participantFrame.ParticipantId, frame.TimestampMs),
                    WardsPlaced = CountUpTo(wardPlacedTimestamps, participantFrame.ParticipantId, frame.TimestampMs),
                    WardsKilled = CountUpTo(wardKilledTimestamps, participantFrame.ParticipantId, frame.TimestampMs)
                });
            }
        }

        return snapshots;
    }

    private static MatchTimelineFrameDto? SelectFrame(List<MatchTimelineFrameDto> frames, int targetMs)
    {
        MatchTimelineFrameDto? best = null;
        var bestDelta = int.MaxValue;

        foreach (var frame in frames)
        {
            var delta = Math.Abs(frame.TimestampMs - targetMs);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = frame;
            }
        }

        return bestDelta <= FrameMatchToleranceMs ? best : null;
    }

    private static void Record(Dictionary<int, List<int>> timestampsByParticipant, int participantId, int timestampMs)
    {
        if (!timestampsByParticipant.TryGetValue(participantId, out var timestamps))
        {
            timestamps = [];
            timestampsByParticipant[participantId] = timestamps;
        }

        timestamps.Add(timestampMs);
    }

    private static int CountUpTo(Dictionary<int, List<int>> timestampsByParticipant, int participantId, int timestampMs)
        => timestampsByParticipant.TryGetValue(participantId, out var timestamps)
            ? timestamps.Count(timestamp => timestamp <= timestampMs)
            : 0;
}
