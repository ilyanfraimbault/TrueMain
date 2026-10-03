using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Directory;

/// <summary>One directory page request, already validated and normalised by the controller.</summary>
public sealed record ChampionDirectoryRequest(
    string? Patch,
    string? EloBracket,
    bool TruemainsOnly,
    string? Position,
    int? ChampionId,
    ChampionDirectorySort Sort,
    bool Descending,
    int Page,
    int PageSize);

public interface IChampionDirectoryQueryService
{
    /// <summary>
    /// One page of the champion directory: the lines of
    /// <see cref="IChampionSummariesQueryService.GetAllSummariesAsync"/> narrowed to the
    /// requested lane and champion, ordered by <see cref="ChampionDirectoryOrdering"/>,
    /// sliced to the page. A page past the end is empty and still carries the real total.
    /// </summary>
    Task<ChampionDirectoryPageReadModel> GetPageAsync(ChampionDirectoryRequest request, CancellationToken ct);
}

/// <summary>
/// The paged directory behind <c>GET /champions/directory</c> (#1734). Owns no SQL: it
/// reads the same cached summaries as <c>GET /champions</c>, the tier list and the
/// homepage, and narrows and orders them in memory.
///
/// <para>
/// The ordered list goes through <see cref="IChampionReadCache"/> like every champion read
/// (#1368) — keyed on the filters and the order, <em>not</em> the page, so paging through
/// one ordering is a slice of one entry rather than one entry per page. That keeps the
/// entry count to what visitors actually ask for (a lane, a sort, a direction) instead of
/// multiplying it by every page of every combination in the shared size-limited cache.
/// </para>
/// </summary>
public sealed class ChampionDirectoryQueryService(
    IChampionSummariesQueryService summariesQueryService,
    IChampionReadCache cache) : IChampionDirectoryQueryService
{
    public async Task<ChampionDirectoryPageReadModel> GetPageAsync(
        ChampionDirectoryRequest request, CancellationToken ct)
    {
        var (patchVersion, ordered) = await cache.GetOrComputeAsync(
            OrderingKey(request),
            token => ComputeOrderingAsync(request, token),
            ct);

        // In longs: a hand-typed page near int.MaxValue would overflow the offset.
        var offset = (long)(request.Page - 1) * request.PageSize;
        var rows = offset >= ordered.Count
            ? Array.Empty<ChampionSummaryReadModel>()
            : ordered.Skip((int)offset).Take(request.PageSize).ToArray();

        return new ChampionDirectoryPageReadModel
        {
            Rows = rows,
            Page = request.Page,
            PageSize = request.PageSize,
            Total = ordered.Count,
            PatchVersion = patchVersion,
        };
    }

    private async Task<(string PatchVersion, IReadOnlyList<ChampionSummaryReadModel> Lines)> ComputeOrderingAsync(
        ChampionDirectoryRequest request, CancellationToken ct)
    {
        var result = await summariesQueryService.GetAllSummariesAsync(
            request.Patch, request.EloBracket, request.TruemainsOnly, ct);

        IEnumerable<ChampionSummaryReadModel> lines = result.Summaries;
        if (request.Position is { } position)
        {
            lines = lines.Where(line => string.Equals(line.Position, position, StringComparison.Ordinal));
        }

        if (request.ChampionId is { } championId)
        {
            lines = lines.Where(line => line.ChampionId == championId);
        }

        return (result.PatchVersion, ChampionDirectoryOrdering.Apply(lines, request.Sort, request.Descending));
    }

    private static string OrderingKey(ChampionDirectoryRequest request)
        => string.Join(
            ':',
            "champions:directory",
            request.Patch ?? "_",
            request.EloBracket ?? "_",
            request.TruemainsOnly ? "truemains" : "everyone",
            request.Position ?? "_",
            request.ChampionId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "_",
            request.Sort,
            request.Descending ? "desc" : "asc");
}
