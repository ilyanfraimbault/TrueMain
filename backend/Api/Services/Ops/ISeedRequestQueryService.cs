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
}
