using Data.Entities;

namespace Data.Repositories;

public interface IRiotAccountRepository
{
    Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct);
    Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct);
    Task<List<RiotAccount>> GetAccountsForRefreshAsync(int batchSize, CancellationToken ct);
    Task<List<AccountKey>> GetAccountsForMainAnalysisAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    Task<List<AccountKey>> ClaimAccountsForMatchIngestAsync(List<string> platforms, int batchSize, CancellationToken ct);
    Task<int> SetMatchIngestStatusAsync(string platformId, string puuid, MatchIngestStatus status, CancellationToken ct);
    Task UpdateLastMatchIngestAtAsync(string platformId, string puuid, DateTime atUtc, CancellationToken ct);

    void Add(RiotAccount account);
}
