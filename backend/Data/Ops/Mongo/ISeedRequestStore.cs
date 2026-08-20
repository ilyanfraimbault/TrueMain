using Data.Entities;

namespace Data.Ops.Mongo;

/// <summary>
/// One page of seed requests and the total matching the same filters. <c>Total</c>
/// is counted with the page's filters but without its skip/take, so the caller can
/// render a pager; it is a <c>long</c> because the queue is fed in bulk and is not
/// bounded by anything an operator types.
/// </summary>
/// <param name="Requests">The page, newest-first.</param>
/// <param name="Total">Rows matching the filters across all pages.</param>
public readonly record struct SeedRequestPage(
    IReadOnlyList<SeedRequestDocument> Requests,
    long Total);

/// <summary>
/// Mongo-backed store for the "seed by Riot ID" intake queue (the
/// <c>seed_requests</c> collection). Unlike the observability stores this is
/// <em>functional</em> data — an operator's explicit request — so writes throw
/// when Mongo is not configured instead of silently dropping the request; reads
/// degrade to empty so panels render rather than 500.
/// </summary>
public interface ISeedRequestStore
{
    /// <summary>Inserts a new request. Throws when the store is inactive.</summary>
    Task InsertAsync(SeedRequestDocument request, CancellationToken ct);

    /// <summary>
    /// The oldest unprocessed (Pending or Resolving) request for this Riot ID on
    /// this platform, matched case-insensitively on name/tag — the idempotency
    /// check behind the create endpoint. Null when none exists.
    /// </summary>
    Task<SeedRequestDocument?> FindUnprocessedByRiotIdAsync(
        string gameName,
        string tagLine,
        string platformId,
        CancellationToken ct);

    Task<SeedRequestDocument?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Recent requests, newest-first (requested desc, id desc), optionally
    /// filtered by exact status and a case-insensitive contains-search on game
    /// name or tag line.
    /// <para>
    /// The unpaged scan, kept for the account explorer, which asks a different
    /// question — "is this one Riot ID anywhere in the newest N requests?" — and
    /// would pay for a count it never reads if it went through
    /// <see cref="GetPageAsync"/>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SeedRequestDocument>> GetRecentAsync(
        SeedRequestStatus? status,
        string? search,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// One page of requests in the same newest-first order, plus the total number
    /// matching the filters, for the admin list (#1166).
    /// <para>
    /// Paged rather than capped because the queue is no longer operator-sized: a
    /// weekly OTP seeder run adds tens of thousands of rows at once, and a list
    /// that can only ever show its newest 200 cannot say how much is left to
    /// drain. <paramref name="platformId"/> filters on the region, which only
    /// became worth having at that volume.
    /// </para>
    /// </summary>
    Task<SeedRequestPage> GetPageAsync(
        SeedRequestStatus? status,
        string? search,
        string? platformId,
        int skip,
        int take,
        CancellationToken ct);

    /// <summary>Up to <paramref name="batchSize"/> Pending requests, oldest-first (FIFO).</summary>
    Task<IReadOnlyList<SeedRequestDocument>> GetPendingAsync(int batchSize, CancellationToken ct);

    /// <summary>
    /// Atomically flips Pending → Resolving. False when another run already
    /// claimed it (or the status changed / the document vanished).
    /// </summary>
    Task<bool> ClaimAsync(Guid id, CancellationToken ct);

    /// <summary>Rolls an interrupted claim back (Resolving → Pending) so a later run can re-claim it.</summary>
    Task<bool> ResetResolvingToPendingAsync(Guid id, CancellationToken ct);

    /// <summary>Stamps the successful terminal state with the resolved identity.</summary>
    Task MarkIngestedAsync(
        Guid id,
        string resolvedPuuid,
        Guid? resolvedRiotAccountId,
        DateTime processedAtUtc,
        CancellationToken ct);

    /// <summary>Stamps the failed terminal state with the (truncated) error.</summary>
    Task MarkFailedAsync(Guid id, string? error, DateTime processedAtUtc, CancellationToken ct);

    /// <summary>
    /// The newest request that resolved to this account (matched on resolved PUUID
    /// + platform), for the candidate detail panel; null for an
    /// organically-discovered account that was never seeded.
    /// </summary>
    Task<SeedRequestDocument?> GetLatestResolvedForAccountAsync(
        string puuid,
        string platformId,
        CancellationToken ct);
}
