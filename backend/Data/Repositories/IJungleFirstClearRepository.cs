using Data.Entities;

namespace Data.Repositories;

public interface IJungleFirstClearRepository
{
    void AddRange(IEnumerable<JungleFirstClear> firstClears);

    Task<int> DeleteByMatchIdAsync(string matchId, CancellationToken ct);

    Task<List<JungleFirstClear>> GetByMatchIdAsync(string matchId, CancellationToken ct);
}
