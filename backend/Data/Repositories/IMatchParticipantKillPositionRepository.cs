using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantKillPositionRepository
{
    void AddRange(IEnumerable<MatchParticipantKillPosition> positions);

    /// <summary>Batch counterpart of the snapshot delete, same rationale (#1229).</summary>
    Task<int> DeleteByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);

    Task<List<MatchParticipantKillPosition>> GetByMatchIdAsync(string matchId, CancellationToken ct);
}
