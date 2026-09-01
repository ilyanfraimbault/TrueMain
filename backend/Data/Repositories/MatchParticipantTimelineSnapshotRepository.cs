using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchParticipantTimelineSnapshotRepository(TrueMainDbContext db)
    : IMatchParticipantTimelineSnapshotRepository
{
    public void AddRange(IEnumerable<MatchParticipantTimelineSnapshot> snapshots)
        => db.MatchParticipantTimelineSnapshots.AddRange(snapshots);

    // Immediate DELETE so re-ingesting a timeline replaces the batch's snapshots
    // without tripping the unique index (avoids the EF insert-before-delete race
    // when the same logical key is removed and re-added in one SaveChanges).
    public Task<int> DeleteByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
        => matchIds.Count == 0
            ? Task.FromResult(0)
            : db.MatchParticipantTimelineSnapshots
                .Where(snapshot => matchIds.Contains(snapshot.MatchId))
                .ExecuteDeleteAsync(ct);

    public Task<List<MatchParticipantTimelineSnapshot>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.MatchId == matchId)
            .OrderBy(snapshot => snapshot.ParticipantId)
            .ThenBy(snapshot => snapshot.IntervalMinute)
            .ToListAsync(ct);
}
