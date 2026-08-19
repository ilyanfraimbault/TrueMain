using Data.Entities;

namespace Data.Repositories;

public interface IMainChampionStatRepository
{
    Task<List<AccountKey>> GetMainAccountsAsync(List<string> platforms, CancellationToken ct);

    /// <summary>
    /// Counts current active mains per (platform, champion) — the coverage signal that
    /// drives candidate scoring, the adaptive <c>IsMain</c> threshold and the per-platform
    /// intake budget.
    /// <para>
    /// It used to aggregate across platforms, on the grounds that champion stats are served
    /// from a cross-platform pool (the public champion endpoints take no region filter), so a
    /// global count reflected a champion page's sample size. That reasoning is what let the
    /// region imbalance persist (#1150): the global count says nothing about *where* the
    /// sample came from, so a champion with 60 EUW1 mains and 1 KR main read as fully covered
    /// and the under-served region got no scarcity signal at all. Sample size is a global
    /// question; coverage is a per-region one.
    /// </para>
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
