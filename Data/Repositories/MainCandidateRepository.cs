using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MainCandidateRepository(TrueMainDbContext db) : IMainCandidateRepository
{
    public Task<List<AccountKey>> GetQueuedAccountsAsync(List<string> platforms, CancellationToken ct)
    {
        return db.MainCandidates
            .Where(c => c.Status == MainCandidateStatus.Queued && platforms.Contains(c.PlatformId))
            .GroupBy(c => new { c.PlatformId, c.Puuid })
            .Select(g => new AccountKey(g.Key.PlatformId, g.Key.Puuid))
            .ToListAsync(ct);
    }

    public Task<int> SetStatusForAccountAsync(string platformId, string puuid, MainCandidateStatus from, MainCandidateStatus to, CancellationToken ct)
    {
        return db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid && c.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, to), ct);
    }

    public Task<List<MainCandidate>> GetByStatusAsync(MainCandidateStatus status, CancellationToken ct)
        => db.MainCandidates.Where(c => c.Status == status).ToListAsync(ct);

    public Task<List<MainCandidate>> GetByPlatformPuuidAndChampionsAsync(string platformId, string puuid, List<int> championIds, CancellationToken ct)
        => db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid && championIds.Contains(c.ChampionId))
            .ToListAsync(ct);

    public Task<List<MainCandidate>> GetScoredByPlatformAsync(string platformId, int take, CancellationToken ct)
        => db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Status == MainCandidateStatus.Scored)
            .OrderByDescending(c => c.Score)
            .Take(Math.Max(0, take))
            .ToListAsync(ct);

    public void Add(MainCandidate candidate)
        => db.MainCandidates.Add(candidate);
}
