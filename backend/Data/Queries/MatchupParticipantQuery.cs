using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Queries;

/// <summary>
/// One participant that faced a given lane opponent, with the columns both matchup
/// readers project from.
/// </summary>
/// <remarks>
/// Object-initializer syntax, not a positional record: Npgsql's EF provider fails to
/// translate a later <c>OrderBy</c>/<c>Select</c> over a positional constructor call
/// re-embedded from a <c>Join</c>'s result selector ("could not be translated").
/// Property-init form translates cleanly.
/// </remarks>
public sealed record MatchupParticipantRow
{
    public required string MatchId { get; init; }

    public required int ParticipantId { get; init; }

    public required int TeamId { get; init; }

    public required bool Win { get; init; }

    public required string Puuid { get; init; }

    public required DateTime GameStartTimeUtc { get; init; }

    public required string GameVersion { get; init; }
}

/// <summary>
/// What "faced champion X in lane" means, spelled as SQL once (#1659).
///
/// <para>
/// Two readers ask it: the matchup-scoped champion page (#923), which folds the builds of
/// a pinned matchup, and the composition-based recommender (#563), whose role opponent is
/// a hard requirement rather than a ranking signal. They used to answer it separately —
/// the recommender loaded the champion's whole retained history and dropped the
/// non-matching games in memory, which is what made a cold recommendation cost seconds.
/// Moving the filter into the database made the two definitions meet, so the join lives
/// here rather than in either caller.
/// </para>
/// </summary>
public static class MatchupParticipantQuery
{
    /// <summary>
    /// Narrows <paramref name="participants"/> to those whose match also holds
    /// <paramref name="opponentChampionId"/> at <paramref name="position"/> on the other
    /// team, in <paramref name="queueId"/> and — when given — on <paramref name="patch"/>.
    /// </summary>
    /// <param name="db">Context the opponent and match sides are read from.</param>
    /// <param name="participants">
    /// Already-narrowed player side (champion, position and any elo filter). Left to the
    /// caller because each reader scopes it differently.
    /// </param>
    /// <param name="opponentChampionId">Champion that must be standing in the other lane.</param>
    /// <param name="position">Canonical Riot position; both sides are matched on it.</param>
    /// <param name="queueId">Queue the match must belong to.</param>
    /// <param name="patch">Optional <c>major.minor</c> patch; null spans the retained window.</param>
    public static IQueryable<MatchupParticipantRow> Facing(
        TrueMainDbContext db,
        IQueryable<MatchParticipant> participants,
        int opponentChampionId,
        string position,
        int queueId,
        string? patch)
    {
        return participants
            .Join(
                db.MatchParticipants.AsNoTracking().Where(o =>
                    o.ChampionId == opponentChampionId && o.TeamPosition == position),
                p => p.MatchId,
                o => o.MatchId,
                (p, o) => new { Participant = p, Opponent = o })
            .Where(pair => pair.Opponent.TeamId != pair.Participant.TeamId)
            .Join(
                db.Matches.AsNoTracking().Where(m => m.QueueId == queueId
                    && (patch == null || m.Patch == patch)),
                pair => pair.Participant.MatchId,
                m => m.Id,
                (pair, m) => new MatchupParticipantRow
                {
                    MatchId = pair.Participant.MatchId,
                    ParticipantId = pair.Participant.ParticipantId,
                    TeamId = pair.Participant.TeamId,
                    Win = pair.Participant.Win,
                    Puuid = pair.Participant.Puuid,
                    GameStartTimeUtc = m.GameStartTimeUtc,
                    GameVersion = m.GameVersion,
                });
    }
}
