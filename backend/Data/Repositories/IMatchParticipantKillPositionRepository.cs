using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantKillPositionRepository
{
    void AddRange(IEnumerable<MatchParticipantKillPosition> positions);

    Task<int> DeleteByMatchIdAsync(string matchId, CancellationToken ct);

    Task<List<MatchParticipantKillPosition>> GetByMatchIdAsync(string matchId, CancellationToken ct);
}
