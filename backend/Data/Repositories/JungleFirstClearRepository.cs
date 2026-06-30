using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class JungleFirstClearRepository(TrueMainDbContext db)
    : IJungleFirstClearRepository
{
    public void AddRange(IEnumerable<JungleFirstClear> firstClears)
        => db.JungleFirstClears.AddRange(firstClears);

    // Immediate DELETE so re-ingesting a match's timeline replaces its first clears
    // without tripping the unique index (avoids the EF insert-before-delete race when
    // the same logical key is removed and re-added in one SaveChanges).
    public Task<int> DeleteByMatchIdAsync(string matchId, CancellationToken ct)
        => db.JungleFirstClears
            .Where(firstClear => firstClear.MatchId == matchId)
            .ExecuteDeleteAsync(ct);

    public Task<List<JungleFirstClear>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.JungleFirstClears
            .AsNoTracking()
            .Where(firstClear => firstClear.MatchId == matchId)
            .OrderBy(firstClear => firstClear.ParticipantId)
            .ToListAsync(ct);
}
