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

    public async Task<Dictionary<string, int>> GetGameDurationSecondsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        return await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameDurationSeconds })
            .ToDictionaryAsync(m => m.Id, m => m.GameDurationSeconds, StringComparer.Ordinal, ct);
    }

    public void Add(Match match)
        => db.Matches.Add(match);
}
