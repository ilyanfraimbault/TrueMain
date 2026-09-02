using Data.Entities;

namespace Data.Repositories;

public interface IRiotAccountRepository
{
    Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct);
    Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct);
    Task<Dictionary<AccountKey, RiotAccount>> GetByKeysAsync(IReadOnlyCollection<AccountKey> accounts, CancellationToken ct);
    Task<bool> ExistsByPuuidAsync(string puuid, CancellationToken ct);

    /// <summary>
    /// Of <paramref name="puuids"/>, the ones already stored with a profile synced at or after
    /// <paramref name="freshSinceUtc"/> — i.e. the accounts a summoner-v4 call would tell us
    /// nothing new about (#1358).
    /// </summary>
    Task<HashSet<string>> GetProfileFreshPuuidsAsync(
        IReadOnlyCollection<string> puuids,
        DateTime freshSinceUtc,
        CancellationToken ct);

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
    /// <para>
    /// <c>platformQuotas</c> is the per-platform slot allocation (#1150), computed by the
    /// caller from the coverage deficit. It is a floor too: a platform that cannot fill its
    /// quota releases the remainder, which is then spread round-robin over the platforms that
    /// can. Before it existed the claim was one cross-platform ordering, so the batch simply
    /// mirrored the account pool — and the pool was ~82% one region.
    /// </para>
    /// </summary>
    Task<List<AccountKey>> ClaimAccountsForMatchIngestAtomicallyAsync(
        IReadOnlyDictionary<string, int> platformQuotas,
        int batchSize,
        double establishedMainShare,
        DateTime nowUtc,
        TimeSpan lease,
        CancellationToken ct);
    /// <summary>
    /// Clears every match-ingest claim whose lease was taken before
    /// <paramref name="leaseCutoffUtc"/> (or that carries no stamp at all), returning those
    /// accounts to <see cref="MatchIngestStatus.Idle"/> and dropping the stale
    /// <see cref="RiotAccount.MatchIngestClaimedAtUtc"/>. Set-based; returns the number of
    /// accounts released.
    /// </summary>
    /// <remarks>
    /// The claim already treats an expired lease as claimable, so this is not what makes the
    /// account reachable again — it is what stops <c>MatchIngestStatus</c> from reading
    /// Processing for accounts that no run holds, which is the state the admin account
    /// explorer and the partial claim index both take at face value (#1344). Same cutoff as
    /// the claim's, passed in by the caller, so the two cannot disagree about "expired".
    /// </remarks>
    Task<int> ReleaseExpiredMatchIngestClaimsAsync(DateTime leaseCutoffUtc, CancellationToken ct);

    Task<int> SetMatchIngestStatusAsync(string platformId, string puuid, MatchIngestStatus status, CancellationToken ct);
    Task UpdateLastMatchIngestAtAsync(string platformId, string puuid, DateTime atUtc, CancellationToken ct);

    void Add(RiotAccount account);
}
