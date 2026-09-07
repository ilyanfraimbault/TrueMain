using Data.Entities;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Accounts;

/// <summary>
/// Read path for the "seed by Riot ID" intake: a single request by id, and the
/// paged queue backing the admin panel's list.
/// </summary>
public interface ISeedRequestQueryService
{
    Task<SeedRequestReadModel?> GetByIdAsync(Guid id, CancellationToken ct);

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

/// <summary>
/// Reads seed requests for the admin "seed by Riot ID" panel, from the Mongo
/// admin store. The optional <c>status</c> filter on the list is an exact match
/// on the <c>SeedRequestStatus</c> name (case-insensitive); an unrecognised value
/// is ignored (no status filter applied) rather than erroring — unlike the region
/// filter, which the controller rejects, because that one is an exact match whose
/// typo would read as an empty region rather than as a bad request.
/// </summary>
public sealed class SeedRequestQueryService(ISeedRequestStore store) : ISeedRequestQueryService
{
    // Mirrors CandidateQueryService so the two lists on the Candidates page page
    // identically.
    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    public async Task<SeedRequestReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var document = await store.GetByIdAsync(id, ct);

        return document is null ? null : ToReadModel(document);
    }

    public async Task<SeedRequestsReadModel> GetPageAsync(
        string? status,
        string? search,
        string? region,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        // Upper bound keeps `(page - 1) * pageSize` within int range even at the
        // maximum page size, mirroring CandidateQueryService.
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        var statusFilter = TryParseStatus(status, out var parsedStatus)
            ? parsedStatus
            : (SeedRequestStatus?)null;

        var result = await store.GetPageAsync(
            statusFilter,
            search,
            region,
            (effectivePage - 1) * effectivePageSize,
            effectivePageSize,
            ct);

        return new SeedRequestsReadModel
        {
            Requests = result.Requests.Select(ToReadModel).ToList(),
            Total = result.Total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    private static bool TryParseStatus(string? status, out SeedRequestStatus parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(status)
            && Enum.TryParse(status.Trim(), ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    // Both the single-by-id and list reads use this so they return an identical
    // shape; the candidate detail read maps through it too.
    internal static SeedRequestReadModel ToReadModel(SeedRequestDocument request)
        => new()
        {
            Id = request.Id,
            GameName = request.GameName,
            TagLine = request.TagLine,
            PlatformId = request.PlatformId,
            Status = request.Status.ToString(),
            Error = request.Error,
            RequestedAtUtc = request.RequestedAtUtc,
            ProcessedAtUtc = request.ProcessedAtUtc,
            ResolvedPuuid = request.ResolvedPuuid,
            ResolvedRiotAccountId = request.ResolvedRiotAccountId
        };
}
