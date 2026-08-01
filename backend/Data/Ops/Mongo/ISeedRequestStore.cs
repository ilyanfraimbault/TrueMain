using Data.Entities;

namespace Data.Ops.Mongo;

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
    /// </summary>
    Task<IReadOnlyList<SeedRequestDocument>> GetRecentAsync(
        SeedRequestStatus? status,
        string? search,
        int limit,
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
