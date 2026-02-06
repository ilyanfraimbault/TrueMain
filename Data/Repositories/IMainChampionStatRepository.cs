using Data.Entities;

namespace Data.Repositories;

public interface IMainChampionStatRepository
{
    Task<List<AccountKey>> GetMainAccountsAsync(List<string> platforms, CancellationToken ct);
    Task<List<MainChampionStat>> GetByAccountAsync(string platformId, string puuid, CancellationToken ct);
    void Add(MainChampionStat stat);
    void Remove(MainChampionStat stat);
}
