using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads <c>SeedRequest</c> rows for the admin "seed by Riot ID" panel. The
/// optional <c>status</c> filter on the list is an exact match on the
/// <c>SeedRequestStatus</c> name (case-insensitive); an unrecognised value is
/// ignored (no status filter applied) rather than erroring.
/// </summary>
public sealed class SeedRequestQueryService(TrueMainDbContext db) : ISeedRequestQueryService
{
    private const int DefaultLimit = 50;
    private const int MinLimit = 1;
    private const int MaxLimit = 200;

    public async Task<SeedRequestReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.SeedRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(request => request.Id == id, ct);

        return entity is null ? null : ToReadModel(entity);
    }

    public async Task<IReadOnlyList<SeedRequestReadModel>> GetRecentAsync(
        string? status,
        string? search,
        int? limit,
        CancellationToken ct)
    {
        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, MinLimit, MaxLimit);

        var query = db.SeedRequests.AsNoTracking();

        if (TryParseStatus(status, out var parsedStatus))
        {
            query = query.Where(request => request.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{LikeEscaping.Escape(search.Trim())}%";
            query = query.Where(request =>
                EF.Functions.ILike(request.GameName, pattern, LikeEscaping.EscapeChar)
                || EF.Functions.ILike(request.TagLine, pattern, LikeEscaping.EscapeChar));
        }

        var entities = await query
            // Newest-first; Id breaks ties so the list is stable when several
            // rows share a RequestedAtUtc.
            .OrderByDescending(request => request.RequestedAtUtc)
            .ThenByDescending(request => request.Id)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        return entities.Select(ToReadModel).ToList();
    }

    private static bool TryParseStatus(string? status, out SeedRequestStatus parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(status)
            && Enum.TryParse(status.Trim(), ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    // Mapped in memory (after materialisation) so Status.ToString() is plain CLR,
    // matching ProcessRunsQueryService. Both the single-by-id and list reads use
    // this so they return an identical shape.
    private static SeedRequestReadModel ToReadModel(SeedRequest request)
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
