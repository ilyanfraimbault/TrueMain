using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchRepository(TrueMainDbContext db) : IMatchRepository
{
    public async Task<HashSet<string>> GetExistingMatchIdsAsync(List<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public void Add(Match match)
        => db.Matches.Add(match);
}
