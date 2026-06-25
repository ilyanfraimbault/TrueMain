using Data.Entities;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Builds per-interval timeline snapshots (issue #525) from a match timeline:
/// one row per participant at each minute mark, with raw values only. The "lead
/// vs lane opponent" is intentionally not computed here — it is a read-time delta
/// against the opposing teamPosition.
/// </summary>
/// <remarks>
/// Sampled every minute (issue #567) so reads can align state to arbitrary event
/// times (item completed, level 6/11/16) and draw a power curve over time — the
/// foundation for the powerspike read. Riot timeline frames are ~1/min, so each
/// minute mark resolves to its own frame. The denser sampling is forward-only;
/// games ingested before this change keep their original 5/10/15/20/30 marks, so
/// reads that need a stable, cross-cohort grid (e.g. timeline-leads) must pin the
/// canonical marks rather than consume every interval.
/// </remarks>
internal static class TimelineSnapshotBuilder
{
    private const int MaxIntervalMinute = 30;

    internal static readonly int[] IntervalMinutes = [.. Enumerable.Range(1, MaxIntervalMinute)];

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
            var type = timelineEvent.Type;
            if (timelineEvent.KillerId is > 0 && type.Equals("CHAMPION_KILL", StringComparison.OrdinalIgnoreCase))
            {
                Record(killTimestamps, timelineEvent.KillerId.Value, timelineEvent.TimestampMs);
            }
            else if (timelineEvent.CreatorId is > 0 && type.Equals("WARD_PLACED", StringComparison.OrdinalIgnoreCase))
            {
                Record(wardPlacedTimestamps, timelineEvent.CreatorId.Value, timelineEvent.TimestampMs);
            }
            else if (timelineEvent.KillerId is > 0 && type.Equals("WARD_KILL", StringComparison.OrdinalIgnoreCase))
            {
                Record(wardKilledTimestamps, timelineEvent.KillerId.Value, timelineEvent.TimestampMs);
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

    // Precondition: frames are ordered by ascending TimestampMs — Riot's timeline
    // guarantee, preserved verbatim by RiotTimelineMapper. The |delta| to a fixed
    // target is therefore V-shaped, so once it grows past the minimum we can stop.
    private static MatchTimelineFrameDto? SelectFrame(List<MatchTimelineFrameDto> frames, int targetMs)
    {
        MatchTimelineFrameDto? best = null;
        var bestDelta = int.MaxValue;

        foreach (var frame in frames)
        {
            var delta = Math.Abs(frame.TimestampMs - targetMs);
            if (delta > bestDelta)
            {
                break; // frames are ascending, so the distance to target only grows from here
            }

            bestDelta = delta;
            best = frame;
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
