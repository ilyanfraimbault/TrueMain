using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchParticipantKillPositionRepository(TrueMainDbContext db)
    : IMatchParticipantKillPositionRepository
{
    public void AddRange(IEnumerable<MatchParticipantKillPosition> positions)
        => db.MatchParticipantKillPositions.AddRange(positions);

    // Immediate DELETE so re-ingesting a timeline replaces the batch's positions
    // cleanly (mirrors the snapshot repository's idempotent rewrite).
    public Task<int> DeleteByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
        => matchIds.Count == 0
            ? Task.FromResult(0)
            : db.MatchParticipantKillPositions
                .Where(position => matchIds.Contains(position.MatchId))
                .ExecuteDeleteAsync(ct);

    public Task<List<MatchParticipantKillPosition>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipantKillPositions
            .AsNoTracking()
            .Where(position => position.MatchId == matchId)
            .OrderBy(position => position.ParticipantId)
            .ThenBy(position => position.TimestampMs)
            .ToListAsync(ct);
}
