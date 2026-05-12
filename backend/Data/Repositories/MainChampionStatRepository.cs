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

    public async Task<Dictionary<AccountKey, List<MainChampionStat>>> GetByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        CancellationToken ct)
    {
        var result = new Dictionary<AccountKey, List<MainChampionStat>>();
        if (accounts.Count == 0)
        {
            return result;
        }

        foreach (var grouping in accounts
                     .Distinct()
                     .GroupBy(a => a.PlatformId, StringComparer.OrdinalIgnoreCase))
        {
            var platformId = grouping.Key;
            var puuids = grouping.Select(a => a.Puuid).Distinct(StringComparer.Ordinal).ToList();
            var stats = await db.MainChampionStats
                .Where(s => s.PlatformId == platformId && puuids.Contains(s.Puuid))
                .ToListAsync(ct);

            foreach (var statGroup in stats.GroupBy(s => new AccountKey(s.PlatformId, s.Puuid)))
            {
                result[statGroup.Key] = statGroup.ToList();
            }

            foreach (var account in grouping)
            {
                if (!result.ContainsKey(account))
                {
                    result[account] = [];
                }
            }
        }

        return result;
    }

    public void Add(MainChampionStat stat)
        => db.MainChampionStats.Add(stat);

    public void Remove(MainChampionStat stat)
        => db.MainChampionStats.Remove(stat);
}
