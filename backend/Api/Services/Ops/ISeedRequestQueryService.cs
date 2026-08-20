using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the "seed by Riot ID" intake: a single request by id, and the
/// recent-requests list (optionally filtered by status) backing the admin
/// panel's history. Both project read-models with <c>AsNoTracking</c>.
/// </summary>
public interface ISeedRequestQueryService
{
    Task<SeedRequestReadModel?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Recent seed requests, newest-first, optionally filtered by status and a free
    /// text <paramref name="search"/> over the Riot ID (gameName/tagLine).
    /// </summary>
    /// <param name="status">
    /// Exact <c>SeedRequestStatus</c> name (case-insensitive); null/blank/unknown
    /// applies no status filter.
    /// </param>
    /// <param name="search">
    /// Case-insensitive substring match on gameName or tagLine; null/blank applies
    /// no search filter.
    /// </param>
    /// <param name="limit">Rows to return (clamped to a safe range).</param>
    /// <param name="ct">Request cancellation token.</param>
    Task<IReadOnlyList<SeedRequestReadModel>> GetRecentAsync(
        string? status,
        string? search,
        int? limit,
        CancellationToken ct);

    /// <summary>
    /// One page of seed requests, newest-first, with the total matching the same
    /// filters (#1166). What the admin list reads: the queue is fed in bulk by the
    /// weekly OTP seeder, so a capped "recent" read shows a rounding error of its
    /// own contents and cannot say how much is still pending.
    /// </summary>
    /// <param name="status">
    /// Exact <c>SeedRequestStatus</c> name (case-insensitive); null/blank/unknown
    /// applies no status filter.
    /// </param>
    /// <param name="search">
    /// Case-insensitive substring match on gameName or tagLine; null/blank applies
    /// no search filter.
    /// </param>
    /// <param name="region">
    /// PlatformId (e.g. "EUW1"); null/blank applies no region filter. Unlike
    /// <paramref name="status"/> an unparseable value is <em>rejected</em> upstream
    /// rather than ignored, so a typo cannot silently widen the result set.
    /// </param>
    /// <param name="page">1-based page index (clamped to a safe range).</param>
    /// <param name="pageSize">Rows per page (clamped to a safe range).</param>
    /// <param name="ct">Request cancellation token.</param>
    Task<SeedRequestsReadModel> GetPageAsync(
        string? status,
        string? search,
        string? region,
        int? page,
        int? pageSize,
        CancellationToken ct);
}
