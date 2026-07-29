using Data.Entities;

namespace Data.Repositories;

public interface IMatchBanRepository
{
    void AddRange(IEnumerable<MatchBan> bans);

    Task<List<MatchBan>> GetByMatchIdAsync(string matchId, CancellationToken ct);
}
