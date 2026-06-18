using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchParticipantKillPositionRepository(TrueMainDbContext db)
    : IMatchParticipantKillPositionRepository
{
    public void AddRange(IEnumerable<MatchParticipantKillPosition> positions)
        => db.MatchParticipantKillPositions.AddRange(positions);

    // Immediate DELETE so re-ingesting a timeline replaces the match's positions
    // cleanly (mirrors the snapshot repository's idempotent rewrite).
    public Task<int> DeleteByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipantKillPositions
            .Where(position => position.MatchId == matchId)
            .ExecuteDeleteAsync(ct);

    public Task<List<MatchParticipantKillPosition>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipantKillPositions
            .AsNoTracking()
            .Where(position => position.MatchId == matchId)
            .OrderBy(position => position.ParticipantId)
            .ThenBy(position => position.TimestampMs)
            .ToListAsync(ct);
}
