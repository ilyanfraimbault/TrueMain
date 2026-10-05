using Core.Lol.WinProbability;
using Data.Entities;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Turns an ingested timeline into its <see cref="MatchWinProbability"/> row (#1911): reduces the
/// Riot timeline and the match's participants to the builder's input, runs
/// <see cref="WinProbabilityBuilder"/>, and maps the curve to the stored documents.
/// </summary>
internal static class WinProbabilityIngestion
{
    /// <summary>The match's row, or null when the builder gives the game no curve.</summary>
    public static MatchWinProbability? Build(
        string matchId,
        int gameDurationSeconds,
        IReadOnlyCollection<MatchParticipant> participants,
        MatchTimelineDto timeline)
    {
        var curve = WinProbabilityBuilder.Build(ToTimeline(gameDurationSeconds, participants, timeline));
        if (curve is null)
        {
            return null;
        }

        return new MatchWinProbability
        {
            MatchId = matchId,
            Points = curve.Points
                .Select(point => new MatchWinProbabilityPoint { Ms = point.Ms, P = point.P })
                .ToList(),
            Swings = curve.Swings
                .Select(swing => new MatchWinProbabilitySwing
                {
                    Ms = swing.Ms,
                    Kind = swing.Kind,
                    TeamId = swing.TeamId,
                    Delta = swing.Delta,
                    KillerId = swing.KillerId,
                    VictimId = swing.VictimId,
                    Assists = swing.Assists,
                    Bounty = swing.Bounty,
                    Lane = swing.Lane,
                    TowerType = swing.TowerType,
                    MonsterSubType = swing.MonsterSubType,
                })
                .ToList(),
            Objectives = curve.Objectives
                .Select(objective => new MatchWinProbabilityObjective
                {
                    Ms = objective.Ms,
                    MonsterType = objective.MonsterType,
                    MonsterSubType = objective.MonsterSubType,
                    TeamId = objective.TeamId,
                    Delta = objective.Delta,
                })
                .ToList(),
        };
    }

    /// <summary>The builder's input: creep score is lane minions plus jungle monsters, as everywhere else.</summary>
    internal static WinProbabilityTimeline ToTimeline(
        int gameDurationSeconds,
        IReadOnlyCollection<MatchParticipant> participants,
        MatchTimelineDto timeline)
        => new(
            gameDurationSeconds * 1000,
            participants
                .Select(participant => new WinProbabilityTimelineParticipant(
                    participant.ParticipantId, participant.TeamId, participant.TeamPosition))
                .ToList(),
            timeline.Frames
                .Select(frame => new WinProbabilityTimelineFrame(
                    frame.TimestampMs,
                    frame.ParticipantFrames
                        .Select(player => new WinProbabilityFramePlayer(
                            player.ParticipantId, player.MinionsKilled + player.JungleMinionsKilled, player.Level))
                        .ToList()))
                .ToList(),
            timeline.Events
                .Where(e => e.Type is "CHAMPION_KILL" or "BUILDING_KILL" or "ELITE_MONSTER_KILL")
                .Select(e => new WinProbabilityTimelineEvent
                {
                    Type = e.Type,
                    Ms = e.TimestampMs,
                    KillerId = e.KillerId,
                    VictimId = e.VictimId,
                    AssistIds = e.AssistingParticipantIds,
                    KillerTeamId = e.KillerTeamId,
                    TeamId = e.TeamId,
                    BuildingType = e.BuildingType,
                    LaneType = e.LaneType,
                    TowerType = e.TowerType,
                    MonsterType = e.MonsterType,
                    MonsterSubType = e.MonsterSubType,
                    Bounty = e.Bounty is null && e.ShutdownBounty is null
                        ? null
                        : (e.Bounty ?? 0) + (e.ShutdownBounty ?? 0),
                })
                .ToList());
}
