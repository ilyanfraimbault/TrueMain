using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class RankSnapshotRepository(TrueMainDbContext db) : IRankSnapshotRepository
{
    public void Add(RankSnapshot snapshot)
        => db.RankSnapshots.Add(snapshot);

    public void Update(RankSnapshot snapshot)
        => db.RankSnapshots.Update(snapshot);

    public Task<RankSnapshot?> GetLatestAsync(Guid riotAccountId, CancellationToken ct)
        => db.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == riotAccountId)
            .OrderByDescending(s => s.CapturedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<Dictionary<Guid, RankSnapshot>> GetLatestForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct)
    {
        if (riotAccountIds.Count == 0)
        {
            return new Dictionary<Guid, RankSnapshot>();
        }

        var ids = riotAccountIds.Distinct().ToList();

        var latest = await db.RankSnapshots
            .AsNoTracking()
            .Where(s => ids.Contains(s.RiotAccountId))
            .GroupBy(s => s.RiotAccountId)
            .Select(g => g.OrderByDescending(s => s.CapturedAtUtc).First())
            .ToListAsync(ct);

        return latest.ToDictionary(s => s.RiotAccountId);
    }

    public async Task<Dictionary<Guid, List<(DateTime CapturedAtUtc, string? Tier)>>> GetTierHistoryForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct)
    {
        if (riotAccountIds.Count == 0)
        {
            return [];
        }

        var ids = riotAccountIds.Distinct().ToList();

        var captures = await db.RankSnapshots
            .AsNoTracking()
            .Where(s => ids.Contains(s.RiotAccountId))
            .Select(s => new { s.RiotAccountId, s.CapturedAtUtc, s.Tier })
            .ToListAsync(ct);

        return captures
            .GroupBy(capture => capture.RiotAccountId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(capture => (capture.CapturedAtUtc, (string?)capture.Tier.ToRiotName())).ToList());
    }

    public Task<List<RankSnapshot>> GetHistoryAsync(
        Guid riotAccountId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct)
        => db.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == riotAccountId
                        && s.CapturedAtUtc >= fromUtc
                        && s.CapturedAtUtc <= toUtc)
            .OrderBy(s => s.CapturedAtUtc)
            .ToListAsync(ct);
}
