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
    /// <summary>
    /// Active mains per (platform, champion) — the coverage signal. Region-scoped since
    /// #1150: a champion-only count is dominated by the region we ingest most, so it read
    /// as covered everywhere while under-served regions got no signal at all.
    /// </summary>
    Task<Dictionary<(string PlatformId, int ChampionId), int>> GetMainCountsByPlatformAndChampionAsync(
        CancellationToken ct);
    Task<List<MainChampionStat>> GetByAccountAsync(string platformId, string puuid, CancellationToken ct);
    Task<Dictionary<AccountKey, List<MainChampionStat>>> GetByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        CancellationToken ct);
    void Add(MainChampionStat stat);
    void Remove(MainChampionStat stat);
}
