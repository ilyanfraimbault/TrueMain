using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchWinProbabilityRepository(TrueMainDbContext db) : IMatchWinProbabilityRepository
{
    public void Add(MatchWinProbability winProbability)
        => db.MatchWinProbabilities.Add(winProbability);

    // Immediate DELETE, before the SaveChanges that inserts the fresh rows: the same
    // primary key removed and re-added in one SaveChanges would trip EF's identity map.
    public Task<int> DeleteByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
        => matchIds.Count == 0
            ? Task.FromResult(0)
            : db.MatchWinProbabilities
                .Where(row => matchIds.Contains(row.MatchId))
                .ExecuteDeleteAsync(ct);
}
