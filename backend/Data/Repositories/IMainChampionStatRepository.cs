using Data.Entities;

namespace Data.Repositories;

public interface IMainChampionStatRepository
{
    Task<List<AccountKey>> GetMainAccountsAsync(List<string> platforms, CancellationToken ct);
    Task<Dictionary<int, int>> GetMainCountsByChampionAsync(CancellationToken ct);
    Task<List<MainChampionStat>> GetByAccountAsync(string platformId, string puuid, CancellationToken ct);
    Task<Dictionary<AccountKey, List<MainChampionStat>>> GetByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        CancellationToken ct);
    void Add(MainChampionStat stat);
    void Remove(MainChampionStat stat);
}
