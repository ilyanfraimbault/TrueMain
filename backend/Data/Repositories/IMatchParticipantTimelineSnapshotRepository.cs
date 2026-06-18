using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantTimelineSnapshotRepository
{
    void AddRange(IEnumerable<MatchParticipantTimelineSnapshot> snapshots);

    Task<int> DeleteByMatchIdAsync(string matchId, CancellationToken ct);

    Task<List<MatchParticipantTimelineSnapshot>> GetByMatchIdAsync(string matchId, CancellationToken ct);
}
