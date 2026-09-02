using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantTimelineSnapshotRepository
{
    void AddRange(IEnumerable<MatchParticipantTimelineSnapshot> snapshots);

    /// <summary>
    /// Clears the snapshots of every match in <paramref name="matchIds"/> in one
    /// statement. Set-based on purpose (#1229): the timeline pass assembles its batch
    /// up front, so a per-match delete spent one round-trip per match where the whole
    /// batch needs one.
    /// </summary>
    Task<int> DeleteByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);

    Task<List<MatchParticipantTimelineSnapshot>> GetByMatchIdAsync(string matchId, CancellationToken ct);
}
