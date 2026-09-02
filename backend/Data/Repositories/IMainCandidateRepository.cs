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
    /// <summary>
    /// The <see cref="MainCandidateStatus.Processing"/> → <see cref="MainCandidateStatus.Validated"/>
    /// promotion, stamping <see cref="MainCandidate.ValidatedAtUtc"/> in the same statement.
    /// A separate method rather than a flag on <c>SetStatusForAccountAsync</c> because
    /// this is the only transition that owns that column: every other one leaves it
    /// alone, and the plain status setter used to leave it alone here too, which is why
    /// the column read as "never validated" for every row in production (#1024).
    /// </summary>
    /// <remarks>
    /// A re-validated candidate (reverted to Queued, ingested again) is re-stamped: the
    /// column is when it last cleared ingestion, which is what the queue-latency snapshot
    /// measures against <see cref="MainCandidate.ScoredAtUtc"/> — itself reset on rescoring.
    /// It stays non-null either way, so the never-promoted pruning predicate is unaffected.
    /// </remarks>
    Task<int> MarkValidatedForAccountAsync(string platformId, string puuid, DateTime validatedAtUtc, CancellationToken ct);

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

    /// <summary>
    /// How many candidates sit in <c>Queued</c> per platform — the queue depth the intake cap
    /// is measured against (#1361).
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetQueuedDepthByPlatformAsync(CancellationToken ct);

    /// <summary>
    /// Demotes up to <paramref name="batchSize"/> of the lowest-scored <c>Queued</c> candidates
    /// on <paramref name="platformId"/> back to <c>Scored</c>, and returns how many rows moved
    /// (#1361). Never deletes: a demoted candidate is re-ranked and can be promoted again.
    /// The batch size is explicit so a one-off drain of a very deep queue makes bounded,
    /// committed progress instead of blowing the command timeout in one statement.
    /// </summary>
    Task<int> DemoteLowestScoredQueuedAsync(string platformId, int batchSize, CancellationToken ct);

    /// <summary>
    /// Returns every <see cref="MainCandidateStatus.Processing"/> row that no live claim
    /// stands behind — the account's lease was taken before <paramref name="leaseCutoffUtc"/>,
    /// or the account is Idle, or it no longer exists — to <see cref="MainCandidateStatus.Queued"/>.
    /// Set-based; returns the number of rows released.
    /// </summary>
    /// <remarks>
    /// Processing is a lease state, and every ordinary exit path settles it
    /// (<c>ValidateAsync</c>, <c>RevertAsync</c>, <c>ReleaseUningestableAsync</c>). A hard
    /// stop — an OOM kill, a container restart, a revert that itself failed — has no exit
    /// path, and the claim query cannot recover the rows either: it selects accounts that
    /// hold an active main or a <see cref="MainCandidateStatus.Queued"/> candidate, and an
    /// account whose candidates are <em>all</em> stuck at Processing matches neither. The
    /// leak seals itself, which is why the release has to be its own sweep rather than a
    /// side effect of the next claim (#1344).
    ///
    /// Expressed as "no live claim" rather than "an expired claim" so a candidate whose
    /// account row was deleted is released too, instead of sitting at Processing forever.
    /// </remarks>
    Task<int> ReleaseExpiredClaimsAsync(DateTime leaseCutoffUtc, CancellationToken ct);

    /// <summary>
    /// Of <paramref name="puuids"/> on <paramref name="platformId"/>, the ones that already
    /// carry a candidate row seen at or after <paramref name="seenSinceUtc"/> — i.e. the
    /// accounts whose champion-mastery call would re-read masteries we just stored (#1358).
    /// </summary>
    Task<HashSet<string>> GetPuuidsWithCandidatesSeenSinceAsync(
        string platformId,
        IReadOnlyCollection<string> puuids,
        DateTime seenSinceUtc,
        CancellationToken ct);

    void Add(MainCandidate candidate);
}
