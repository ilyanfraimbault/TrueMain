using Data.Entities;

namespace Data.Repositories;

public interface IMainCandidateRepository
{
    Task<List<AccountKey>> GetQueuedAccountsAsync(List<string> platforms, CancellationToken ct);
    Task<int> SetStatusForAccountAsync(string platformId, string puuid, MainCandidateStatus from, MainCandidateStatus to, CancellationToken ct);
    Task<int> SetStatusForAccountAsync(string platformId, string puuid, IReadOnlyCollection<MainCandidateStatus> from, MainCandidateStatus to, CancellationToken ct);

    /// <summary>
    /// Set-based transition for many accounts at once (#858): one query per
    /// distinct platform in <paramref name="accounts"/> rather than one per
    /// account, so the round-trip count no longer grows with batch size.
    /// Returns exactly the accounts that had at least one <paramref name="from"/>
    /// row transitioned — not a row count, since one account can carry several
    /// candidate rows (one per champion) and must still only count once.
    /// </summary>
    Task<IReadOnlyCollection<AccountKey>> SetStatusForAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        MainCandidateStatus from,
        MainCandidateStatus to,
        CancellationToken ct);
    Task<List<MainCandidate>> GetByStatusAsync(MainCandidateStatus status, CancellationToken ct);
    Task<List<MainCandidate>> GetNewBatchAsync(int batchSize, CancellationToken ct);
    Task<List<MainCandidate>> GetByPlatformPuuidAndChampionsAsync(string platformId, string puuid, List<int> championIds, CancellationToken ct);

    /// <summary>
    /// Tracked candidates for any of the given platforms and puuids, so the harvest can
    /// load every existing candidate it might refresh in one query instead of one per row.
    /// </summary>
    Task<List<MainCandidate>> GetByPlatformsAndPuuidsAsync(
        IReadOnlyCollection<string> platformIds,
        IReadOnlyCollection<string> puuids,
        CancellationToken ct);

    /// <summary>
    /// The platform's best-scored candidates awaiting promotion, highest score first.
    /// <c>deprioritizedChampionIds</c> — the champions already at or above the coverage
    /// target (#900) — sort to the back of the queue whatever their score, so they only
    /// take the slots the under-covered champions leave free. A priority, not a filter:
    /// they are still promoted when the rest of the pool does not fill <c>take</c>.
    /// </summary>
    Task<List<MainCandidate>> GetScoredByPlatformAsync(
        string platformId,
        int take,
        IReadOnlyCollection<int> deprioritizedChampionIds,
        CancellationToken ct);

    /// <summary>
    /// Deletes never-promoted candidates that have gone stale (#487): rows still in a
    /// pre-ingestion or rejected status (<see cref="MainCandidateStatus.New"/>,
    /// <see cref="MainCandidateStatus.Scored"/>, <see cref="MainCandidateStatus.Rejected"/>),
    /// never validated, and last active before <paramref name="lastPlayCutoffUtc"/>. Set-based
    /// delete; returns the number of rows removed. In-flight (Queued/Processing) and Validated
    /// candidates are never touched. Bounds <c>main_candidates</c> growth from the harvest.
    /// </summary>
    Task<int> PruneStaleNeverPromotedAsync(DateTime lastPlayCutoffUtc, CancellationToken ct);

    void Add(MainCandidate candidate);
}
