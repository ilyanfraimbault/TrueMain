using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchBanRepository(TrueMainDbContext db)
    : IMatchBanRepository
{
    public void AddRange(IEnumerable<MatchBan> bans)
        => db.MatchBans.AddRange(bans);

    // No delete-by-match counterpart, unlike the timeline-sourced children: bans are
    // written once with their match, in the same insert, and a match that already
    // exists is filtered out upstream by ExistingMatchScanner rather than re-persisted.
    public Task<List<MatchBan>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchBans
            .AsNoTracking()
            .Where(ban => ban.MatchId == matchId)
            .OrderBy(ban => ban.PickTurn)
            .ToListAsync(ct);
}
