using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MainChampionStatRepository(TrueMainDbContext db) : IMainChampionStatRepository
{
    public Task<List<AccountKey>> GetMainAccountsAsync(List<string> platforms, CancellationToken ct)
    {
        return db.MainChampionStats
            .Where(s => s.IsMain && platforms.Contains(s.PlatformId))
            .GroupBy(s => new { s.PlatformId, s.Puuid })
            .Select(g => new AccountKey(g.Key.PlatformId, g.Key.Puuid))
            .ToListAsync(ct);
    }

    public Task<List<MainChampionStat>> GetByAccountAsync(string platformId, string puuid, CancellationToken ct)
        => db.MainChampionStats
            .Where(s => s.PlatformId == platformId && s.Puuid == puuid)
            .ToListAsync(ct);

    public void Add(MainChampionStat stat)
        => db.MainChampionStats.Add(stat);

    public void Remove(MainChampionStat stat)
        => db.MainChampionStats.Remove(stat);
}
