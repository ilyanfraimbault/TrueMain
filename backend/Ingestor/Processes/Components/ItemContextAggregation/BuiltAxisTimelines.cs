using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Loads, for one batch, the item timelines the in-game axes (#1750) replay: those of the
/// enemies of every cohort member.
/// </summary>
/// <remarks>
/// <para>
/// <b>One query per side, never one per match.</b> The enemies are asked for by team — "the
/// participants of team T in these matches" — with every match of the batch that needs that
/// team in one array, so a batch costs two round trips however many games it folds.
/// </para>
/// <para>
/// <b>The working set stays at the batch's.</b> Only the timeline column is projected, only
/// for the teams a cohort member actually faced, and only for the untracked participants: a
/// tracked player's timeline is already in hand, loaded with the builds. A match with a
/// cohort member on one side loads five timelines more than before this axis existed, one with
/// members on both sides ten at most — the bound the pattern fold lacked when it was OOM-killed
/// (#600).
/// </para>
/// </remarks>
public static class BuiltAxisTimelines
{
    /// <summary>
    /// The timelines of every untracked participant on the team facing one of
    /// <paramref name="members"/>, keyed by match and participant.
    /// </summary>
    /// <param name="db">The batch's context.</param>
    /// <param name="members">Each cohort member's match and team.</param>
    /// <param name="teams">Every team of the batch's matches.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<Dictionary<(string MatchId, int ParticipantId), List<ItemEvent>>> LoadAsync(
        TrueMainDbContext db,
        IEnumerable<(string MatchId, int TeamId)> members,
        IEnumerable<(string MatchId, int TeamId)> teams,
        CancellationToken ct)
    {
        var memberTeams = members
            .GroupBy(member => member.MatchId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(member => member.TeamId).ToHashSet(), StringComparer.Ordinal);

        // A team is needed when somebody on the other side of its match is a cohort member.
        var needed = teams
            .Distinct()
            .Where(team => memberTeams.TryGetValue(team.MatchId, out var sides) && sides.Any(side => side != team.TeamId))
            .ToList();

        var timelines = new Dictionary<(string, int), List<ItemEvent>>();

        foreach (var side in needed.GroupBy(p => p.TeamId))
        {
            var teamId = side.Key;
            var matchIds = side.Select(team => team.MatchId).ToList();

            var rows = await db.MatchParticipants
                .AsNoTracking()
                .Where(p => p.TeamId == teamId && p.RiotAccountId == null && matchIds.Contains(p.MatchId))
                .Select(p => new { p.MatchId, p.ParticipantId, p.ItemEvents })
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                timelines[(row.MatchId, row.ParticipantId)] = row.ItemEvents;
            }
        }

        return timelines;
    }
}
