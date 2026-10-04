using Core.Lol.Map;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// Read path for the admin data-quality panel's per-match drill-down. Read-only
/// diagnostics — no repair.
/// </summary>
public interface IMatchDataQualityDetailQueryService
{
    /// <summary>
    /// Per-match detail: both teams by position with the gaps identified, plus
    /// the issue types the match trips. Null when no such match exists.
    /// </summary>
    Task<MatchDataQualityDetailReadModel?> GetMatchDetailAsync(string matchId, CancellationToken ct);
}

/// <summary>
/// Lays one match's teams out by position and judges it with the same
/// <see cref="MatchDataQualityRules"/> as the incomplete-matches list, so the
/// drill-down and the list agree on what the match trips.
/// </summary>
public sealed class MatchDataQualityDetailQueryService(TrueMainDbContext db) : IMatchDataQualityDetailQueryService
{
    public async Task<MatchDataQualityDetailReadModel?> GetMatchDetailAsync(string matchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matchId))
        {
            return null;
        }

        var trimmedId = matchId.Trim();

        var match = await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == trimmedId)
            .Select(m => new
            {
                m.Id,
                m.PlatformId,
                m.QueueId,
                m.GameMode,
                m.GameStartTimeUtc,
                m.GameDurationSeconds,
                m.GameVersion,
                m.TimelineIngested
            })
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            return null;
        }

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.MatchId == trimmedId)
            .OrderBy(p => p.TeamId)
            .ThenBy(p => p.ParticipantId)
            .Select(p => new ParticipantRow(
                p.ParticipantId,
                p.TeamId,
                p.TeamPosition,
                p.ChampionId,
                p.SummonerName,
                p.Win))
            .ToListAsync(ct);

        var profile = QueueDataQualityProfile.ForQueue(match.QueueId);
        var summary = MatchSummary.From(
            match.Id,
            match.PlatformId,
            match.QueueId,
            match.GameStartTimeUtc,
            match.GameDurationSeconds,
            match.TimelineIngested,
            participants,
            profile);
        var issues = MatchDataQualityRules.Evaluate(
            summary,
            MatchDataQualityRules.StaleTimelineCutoff(DateTime.UtcNow));

        var teams = BuildTeams(participants, profile);

        return new MatchDataQualityDetailReadModel
        {
            MatchId = match.Id,
            PlatformId = match.PlatformId,
            QueueId = match.QueueId,
            GameMode = match.GameMode,
            GameStartTimeUtc = match.GameStartTimeUtc,
            GameDurationSeconds = match.GameDurationSeconds,
            GameVersion = match.GameVersion,
            TimelineIngested = match.TimelineIngested,
            ParticipantCount = summary.ParticipantCount,
            ExpectedParticipantCount = profile.IsKnown ? profile.ExpectedParticipants : null,
            QueueKnown = profile.IsKnown,
            HasLanes = profile.HasLanes,
            Issues = issues.Select(i => MatchDataQualityRules.ToWireName(i)).ToList(),
            Teams = teams
        };
    }

    private static IReadOnlyList<MatchTeamReadModel> BuildTeams(
        IReadOnlyList<ParticipantRow> participants,
        QueueDataQualityProfile profile)
    {
        var presentTeamIds = participants.Select(p => p.TeamId).Distinct().ToList();

        // For a known two-team queue (SR/ARAM) always surface BOTH standard team
        // ids, even when a team has zero ingested rows — otherwise a half-missing
        // match would hide its absent team entirely, and the operator couldn't see
        // that team's missing lane slots. Any non-standard team ids actually
        // present (odd data) are appended after.
        var includeBothStandardTeams = profile.IsKnown && profile.TeamCount == 2;
        var teamIds = (includeBothStandardTeams
                ? QueueDataQualityProfile.StandardTeamIds
                : QueueDataQualityProfile.StandardTeamIds.Where(presentTeamIds.Contains))
            .Concat(presentTeamIds.Where(id => !QueueDataQualityProfile.StandardTeamIds.Contains(id)))
            .ToList();

        if (teamIds.Count == 0)
        {
            return [];
        }

        // Per-team headcount expectation, only meaningful for a profiled queue's
        // standard teams (a non-standard team id is an anomaly with no expected
        // size of its own).
        int? expectedPlayersPerTeam = profile.IsKnown && profile.TeamCount > 0
            ? profile.ExpectedParticipants / profile.TeamCount
            : null;

        var teams = new List<MatchTeamReadModel>();
        foreach (var teamId in teamIds)
        {
            var members = participants.Where(p => p.TeamId == teamId).ToList();

            // Champions that appear more than once on this team (duplicate signal).
            var duplicateChampions = members
                .GroupBy(m => m.ChampionId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            var unplacedCount = 0;
            List<MatchSlotReadModel> slots;
            if (profile.HasLanes)
            {
                // Lay the team across the five canonical lanes, flagging gaps and
                // any participant whose champion repeats on the team.
                slots = QueueDataQualityProfile.LanePositions
                    .Select(position =>
                    {
                        var occupant = members.FirstOrDefault(m =>
                            string.Equals(m.TeamPosition, position, StringComparison.OrdinalIgnoreCase));
                        return occupant is null
                            ? new MatchSlotReadModel { Position = position, Filled = false }
                            : new MatchSlotReadModel
                            {
                                Position = position,
                                Filled = true,
                                ParticipantId = occupant.ParticipantId,
                                ChampionId = occupant.ChampionId,
                                SummonerName = occupant.SummonerName,
                                Win = occupant.Win,
                                DuplicateChampion = duplicateChampions.Contains(occupant.ChampionId)
                            };
                    })
                    .ToList();

                // Surface any participant with an unrecognised/empty TeamPosition
                // that didn't map onto a canonical lane, so laned-queue data with
                // a bad position isn't silently dropped from the layout.
                var placed = slots
                    .Where(s => s.ParticipantId is not null)
                    .Select(s => s.ParticipantId!.Value)
                    .ToHashSet();
                var unplacedMembers = members
                    .Where(m => !placed.Contains(m.ParticipantId))
                    .ToList();
                unplacedCount = unplacedMembers.Count;
                slots.AddRange(unplacedMembers
                    .Select(m => new MatchSlotReadModel
                    {
                        Position = string.IsNullOrWhiteSpace(m.TeamPosition) ? "UNKNOWN" : m.TeamPosition,
                        Filled = true,
                        ParticipantId = m.ParticipantId,
                        ChampionId = m.ChampionId,
                        SummonerName = m.SummonerName,
                        Win = m.Win,
                        DuplicateChampion = duplicateChampions.Contains(m.ChampionId)
                    }));
            }
            else
            {
                // Laneless queue: one slot per participant, in roster order.
                slots = members
                    .Select(m => new MatchSlotReadModel
                    {
                        Position = string.Empty,
                        Filled = true,
                        ParticipantId = m.ParticipantId,
                        ChampionId = m.ChampionId,
                        SummonerName = m.SummonerName,
                        Win = m.Win,
                        DuplicateChampion = duplicateChampions.Contains(m.ChampionId)
                    })
                    .ToList();
            }

            teams.Add(new MatchTeamReadModel
            {
                TeamId = teamId,
                PlayerCount = members.Count,
                ExpectedPlayerCount = QueueDataQualityProfile.StandardTeamIds.Contains(teamId)
                    ? expectedPlayersPerTeam
                    : null,
                UnplacedCount = unplacedCount,
                // All members of a team share the same result; null when empty.
                Win = members.Count > 0 ? members[0].Win : null,
                Slots = slots
            });
        }

        return teams;
    }
}
