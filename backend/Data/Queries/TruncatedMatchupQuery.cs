using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Queries;

/// <summary>
/// "Faced champion X in lane", asked of the truncated seats matches leave behind past
/// retention (#2368) — the same question <see cref="MatchupParticipantQuery"/> asks of the
/// live rows. Remakes never reach the table, so no game filter is needed here.
/// </summary>
public static class TruncatedMatchupQuery
{
    /// <summary>
    /// The seats of <paramref name="championId"/> at <paramref name="position"/>, in
    /// <paramref name="queueId"/>, on <paramref name="patch"/> when given and in the rank bands
    /// of <paramref name="bracketFilter"/> when given.
    /// </summary>
    public static IQueryable<TruncatedMatchSeat> Seats(
        TrueMainDbContext db,
        int championId,
        string position,
        int queueId,
        string? patch,
        IReadOnlyList<string>? bracketFilter)
    {
        ArgumentNullException.ThrowIfNull(db);

        var seats = db.TruncatedMatchSeats
            .AsNoTracking()
            .Where(seat => seat.ChampionId == championId
                && seat.TeamPosition == position
                && seat.QueueId == queueId
                && (patch == null || seat.Patch == patch));

        return bracketFilter is null ? seats : seats.Where(seat => bracketFilter.Contains(seat.EloBracket));
    }

    /// <summary>
    /// The seats of <paramref name="championId"/> at <paramref name="position"/> whose match
    /// held <paramref name="opponentChampionId"/> at the same position on the other team, in
    /// <paramref name="queueId"/>, on <paramref name="patch"/> when given and in the rank bands
    /// of <paramref name="bracketFilter"/> when given.
    /// </summary>
    public static IQueryable<TruncatedMatchSeat> Facing(
        TrueMainDbContext db,
        int championId,
        int opponentChampionId,
        string position,
        int queueId,
        string? patch,
        IReadOnlyList<string>? bracketFilter)
    {
        return Seats(db, championId, position, queueId, patch, bracketFilter).Where(seat => db.TruncatedMatchSeats.Any(opponent =>
            opponent.MatchId == seat.MatchId
            && opponent.ChampionId == opponentChampionId
            && opponent.TeamPosition == position
            && opponent.TeamId != seat.TeamId));
    }
}
