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
    ///
    /// <paramref name="eloBracket"/> is a cumulative "X+" threshold (see
    /// <see cref="Core.Lol.Ranking.EloBracket"/>); null / ALL spans every band.
    /// </summary>
    Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(
        string? patch, string? eloBracket, CancellationToken ct);
}
