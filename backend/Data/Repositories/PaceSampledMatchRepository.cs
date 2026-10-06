using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class PaceSampledMatchRepository(TrueMainDbContext db) : IPaceSampledMatchRepository
{
    public async Task<HashSet<string>> GetSampledAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = await db.PaceSampledMatches
            .AsNoTracking()
            .Where(match => matchIds.Contains(match.MatchId))
            .Select(match => match.MatchId)
            .ToListAsync(ct);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public void Add(PaceSampledMatch match)
        => db.PaceSampledMatches.Add(match);

    public Task<int> DeleteSampledBeforeAsync(DateTime cutoffUtc, CancellationToken ct)
        => db.PaceSampledMatches
            .Where(match => match.SampledAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);
}
