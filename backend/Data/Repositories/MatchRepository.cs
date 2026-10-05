using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchRepository(TrueMainDbContext db) : IMatchRepository
{
    public async Task<HashSet<string>> GetExistingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<HashSet<string>> GetTimelinePendingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var pendingIds = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id) && !m.TimelineIngested)
            .Select(m => m.Id)
            .ToListAsync(ct);

        return pendingIds.ToHashSet(StringComparer.Ordinal);
    }

    public Task SetTimelineIngestedAsync(IReadOnlyCollection<string> matchIds, bool timelineIngested, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.TimelineIngested, timelineIngested),
                ct);
    }

    public void Add(Match match)
        => db.Matches.Add(match);

    public async Task<List<PaceBenchmarkFoldClaim>> ClaimPaceBenchmarkFoldAsync(
        IReadOnlyCollection<string> matchIds,
        CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return [];
        }

        var ids = matchIds.Distinct(StringComparer.Ordinal).ToArray();

        return await db.Database.SqlQuery<PaceBenchmarkFoldClaim>(
                $"""
                 UPDATE matches
                 SET "PaceBenchmarkAggregated" = true
                 WHERE "Id" = ANY({ids}) AND NOT "PaceBenchmarkAggregated"
                 RETURNING "Id", "Patch", "GameStartTimeUtc", "GameDurationSeconds"
                 """)
            .ToListAsync(ct);
    }
}
