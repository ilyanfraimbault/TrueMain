using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the admin Candidates panel: the main-candidate ingestion
/// pipeline list (filterable/searchable/paged) and a single candidate's detail
/// (its pipeline fields plus the joined account, ingested match count, and the
/// linked manual seed request when one exists). Projects read-models with
/// <c>AsNoTracking</c>; no generic repository.
/// </summary>
public interface ICandidateQueryService
{
    /// <summary>
    /// A page of main candidates, newest-first, optionally filtered and searched.
    /// </summary>
    /// <param name="status">
    /// Restrict to a single <c>MainCandidateStatus</c> (case-insensitive name);
    /// null/blank/unknown means all statuses.
    /// </param>
    /// <param name="platformId">
    /// Restrict to one platform/region (e.g. "EUW1", case-insensitive); null/blank
    /// means all regions.
    /// </param>
    /// <param name="search">
    /// Free-text search: matches the joined Riot ID (gameName/tagLine), the PUUID,
    /// or — when the term is numeric — the champion id. Null/blank means no search.
    /// </param>
    /// <param name="page">1-based page index (clamped to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (clamped to a safe range).</param>
    /// <param name="ct">Request cancellation token.</param>
    Task<CandidatesReadModel> GetCandidatesAsync(
        string? status,
        string? platformId,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct);

    /// <summary>
    /// Detail for one candidate by id: its pipeline fields, the joined account
    /// identity, the ingested match count for its PUUID, and the linked
    /// <c>SeedRequest</c> when one matches its <c>ResolvedPuuid</c> + platform.
    /// Null when no candidate has the given id.
    /// </summary>
    Task<CandidateDetailReadModel?> GetByIdAsync(Guid id, CancellationToken ct);
}
