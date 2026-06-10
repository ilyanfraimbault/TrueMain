using Data.Entities;

namespace Data.Repositories;

public interface IMainCandidateRepository
{
    Task<List<AccountKey>> GetQueuedAccountsAsync(List<string> platforms, CancellationToken ct);
    Task<int> SetStatusForAccountAsync(string platformId, string puuid, MainCandidateStatus from, MainCandidateStatus to, CancellationToken ct);
    Task<int> SetStatusForAccountAsync(string platformId, string puuid, IReadOnlyCollection<MainCandidateStatus> from, MainCandidateStatus to, CancellationToken ct);
    Task<List<MainCandidate>> GetByStatusAsync(MainCandidateStatus status, CancellationToken ct);
    Task<List<MainCandidate>> GetNewBatchAsync(int batchSize, CancellationToken ct);
    Task<List<MainCandidate>> GetByPlatformPuuidAndChampionsAsync(string platformId, string puuid, List<int> championIds, CancellationToken ct);
    Task<List<MainCandidate>> GetScoredByPlatformAsync(string platformId, int take, CancellationToken ct);
    void Add(MainCandidate candidate);
}
