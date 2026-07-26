using Data.Entities;

namespace Data.Repositories;

public interface IRiotAccountRepository
{
    Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct);
    Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct);
    Task<Dictionary<AccountKey, RiotAccount>> GetByKeysAsync(IReadOnlyCollection<AccountKey> accounts, CancellationToken ct);
    Task<bool> ExistsByPuuidAsync(string puuid, CancellationToken ct);

    /// <summary>
    /// The subset of <paramref name="puuids"/> that already have an account, in one query.
    /// Lets the harvest create minimal accounts for unknown puuids without a lookup per row.
    /// </summary>
    Task<HashSet<string>> GetExistingPuuidsAsync(IReadOnlyCollection<string> puuids, CancellationToken ct);

    Task<List<RiotAccount>> GetAccountsForRefreshAsync(int batchSize, CancellationToken ct);
    Task<List<AccountKey>> GetAccountsForMainAnalysisAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// Accounts holding at least one <c>IsMain</c> stat whose champion-mastery activity
    /// check is due (never checked first, then oldest). Includes accounts already marked
    /// inactive — that check is the only path back to active (#900).
    /// </summary>
    Task<List<AccountKey>> GetAccountsForActivityCheckAsync(DateTime cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// Atomically claims the next accounts to ingest matches for.
    /// <c>establishedMainShare</c> is the share of the batch reserved for accounts that are
    /// already active established mains, the remainder going to <c>Queued</c> candidates
    /// (#900) — a floor, not a partition: whatever one class cannot fill spills to the other.
    /// </summary>
    Task<List<AccountKey>> ClaimAccountsForMatchIngestAtomicallyAsync(
        IReadOnlyCollection<string> platforms,
        int batchSize,
        double establishedMainShare,
        DateTime nowUtc,
        TimeSpan lease,
        CancellationToken ct);
    Task<int> SetMatchIngestStatusAsync(string platformId, string puuid, MatchIngestStatus status, CancellationToken ct);
    Task UpdateLastMatchIngestAtAsync(string platformId, string puuid, DateTime atUtc, CancellationToken ct);

    void Add(RiotAccount account);
}
