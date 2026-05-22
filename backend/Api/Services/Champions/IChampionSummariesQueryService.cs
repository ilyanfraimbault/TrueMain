using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionSummariesQueryService
{
    /// <summary>
    /// Lightweight directory query: one <see cref="ChampionSummaryReadModel"/>
    /// per <c>(champion, position)</c> pair on the active queue, all rows
    /// pinned to a single patch (<paramref name="patch"/> if non-null and
    /// canonical, otherwise the global latest patch in the aggregate table).
    /// Used by the champions list / index page; callers that need builds,
    /// runes or patterns go through <c>GET /champions/{id}</c>.
    /// </summary>
    Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(string? patch, CancellationToken ct);

    /// <summary>
    /// Paged variant of <see cref="GetAllSummariesAsync"/> — returns the
    /// requested page (1-indexed) of the cached, already-sorted directory.
    /// <paramref name="page"/> and <paramref name="pageSize"/> are clamped:
    /// pages past the end yield an empty <c>Items</c> list with the real
    /// <c>TotalCount</c> so the caller can re-anchor on the last page.
    /// </summary>
    Task<ChampionSummariesPagedResponse> GetSummariesPageAsync(
        string? patch,
        int page,
        int pageSize,
        CancellationToken ct);
}
