using Data.Entities;

namespace Data.Repositories;

public interface IMatchRepository
{
    Task<HashSet<string>> GetExistingMatchIdsAsync(List<string> matchIds, CancellationToken ct);
    void Add(Match match);
}
