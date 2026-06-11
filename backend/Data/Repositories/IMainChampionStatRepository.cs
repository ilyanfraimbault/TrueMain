using Data.Entities;

namespace Data.Repositories;

public interface IMainChampionStatRepository
{
    Task<List<AccountKey>> GetMainAccountsAsync(List<string> platforms, CancellationToken ct);

    /// <summary>
    /// Counts current mains per champion, aggregated across all platforms (global).
    /// This is intentional: champion stats are served from a cross-platform pool (the
    /// public champion endpoints take no region filter), so the global main count is the
    /// signal that reflects a champion page's sample size. If region-scoped champion stats
    /// are ever added, this should become per (champion, platform).
    /// </summary>
    Task<Dictionary<int, int>> GetMainCountsByChampionAsync(CancellationToken ct);
    Task<List<MainChampionStat>> GetByAccountAsync(string platformId, string puuid, CancellationToken ct);
    Task<Dictionary<AccountKey, List<MainChampionStat>>> GetByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        CancellationToken ct);
    void Add(MainChampionStat stat);
    void Remove(MainChampionStat stat);
}
