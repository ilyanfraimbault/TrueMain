using Data.Entities;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads seed requests for the admin "seed by Riot ID" panel, from the Mongo
/// admin store. The optional <c>status</c> filter on the list is an exact match
/// on the <c>SeedRequestStatus</c> name (case-insensitive); an unrecognised value
/// is ignored (no status filter applied) rather than erroring.
/// </summary>
public sealed class SeedRequestQueryService(ISeedRequestStore store) : ISeedRequestQueryService
{
    private const int DefaultLimit = 50;
    private const int MinLimit = 1;
    private const int MaxLimit = 200;

    public async Task<SeedRequestReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var document = await store.GetByIdAsync(id, ct);

        return document is null ? null : ToReadModel(document);
    }

    public async Task<IReadOnlyList<SeedRequestReadModel>> GetRecentAsync(
        string? status,
        string? search,
        int? limit,
        CancellationToken ct)
    {
        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, MinLimit, MaxLimit);

        var statusFilter = TryParseStatus(status, out var parsedStatus)
            ? parsedStatus
            : (SeedRequestStatus?)null;

        var documents = await store.GetRecentAsync(statusFilter, search, effectiveLimit, ct);

        return documents.Select(ToReadModel).ToList();
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
