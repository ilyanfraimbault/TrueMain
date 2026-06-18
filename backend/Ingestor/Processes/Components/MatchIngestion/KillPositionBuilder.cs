using Data.Entities;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Builds the bounded set of kill-participation positions from a match timeline
/// (issue #536): for every CHAMPION_KILL before the early-game cutoff, one row per
/// participant involved (killer + assists) at the kill location. Deliberately
/// bounded so the table stays small — only kill participations, only early game.
/// </summary>
internal static class KillPositionBuilder
{
    // Roam is an early/mid-game signal; kills after 15 minutes happen in teamfights
    // all over the map and say little about a laner roaming. 15 min = 900 000 ms.
    internal const int EarlyGameCutoffMs = 900_000;

    public static List<MatchParticipantKillPosition> Build(string matchId, MatchTimelineDto timeline)
    {
        var positions = new List<MatchParticipantKillPosition>();

        foreach (var timelineEvent in timeline.Events)
        {
            if (!string.Equals(timelineEvent.Type, "CHAMPION_KILL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (timelineEvent.TimestampMs >= EarlyGameCutoffMs)
            {
                continue;
            }

            if (timelineEvent.PositionX is not { } x || timelineEvent.PositionY is not { } y)
            {
                continue;
            }

            // Only the killer and assists are recorded — they chose to be at this
            // location to make a play (the roam signal). The victim is deliberately
            // skipped: dying somewhere isn't roaming to it.
            if (timelineEvent.KillerId is > 0)
            {
                positions.Add(Position(matchId, timelineEvent.KillerId.Value, timelineEvent.TimestampMs, x, y));
            }

            foreach (var assistId in timelineEvent.AssistingParticipantIds)
            {
                if (assistId > 0)
                {
                    positions.Add(Position(matchId, assistId, timelineEvent.TimestampMs, x, y));
                }
            }
        }

        return positions;
    }

    private static MatchParticipantKillPosition Position(string matchId, int participantId, int timestampMs, int x, int y)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            TimestampMs = timestampMs,
            X = x,
            Y = y
        };
}
