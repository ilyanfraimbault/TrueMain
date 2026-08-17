using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionSummariesQueryService
{
    /// <summary>
    /// Lightweight directory query: one <see cref="ChampionSummaryReadModel"/>
    /// per <c>(champion, position)</c> pair on the active queue, all rows
    /// pinned to a single patch (<paramref name="patch"/> if non-null and
    /// canonical, otherwise the global latest patch in the aggregate table),
    /// wrapped with the resolved patch and true total games (#972 — see
    /// <see cref="ChampionSummariesResult"/>). Used by the champions list /
    /// index page and the homepage overview; callers that need builds, runes
    /// or patterns go through <c>GET /champions/{id}</c>.
    ///
    /// <paramref name="eloBracket"/> is a cumulative "X+" threshold (see
    /// <see cref="Core.Lol.Ranking.EloBracket"/>); null / ALL spans every band.
    /// </summary>
    Task<ChampionSummariesResult> GetAllSummariesAsync(
        string? patch, string? eloBracket, CancellationToken ct);

    /// <summary>
    /// Every aggregated game on the tracked queue, all patches summed — the homepage's
    /// lifetime volume chip. Includes below-floor and position-less scopes, the same
    /// population <see cref="ChampionSummariesResult.TotalGames"/> counts for one patch.
    ///
    /// <para>
    /// One <c>SUM</c> pushed to SQL rather than a directory per patch: the number is a
    /// scalar, and the patches behind it only ever grow. Cached for half an hour so the
    /// most-hit page on the site does not scan the aggregate table per request.
    /// </para>
    /// </summary>
    Task<long> GetTotalGamesAsync(CancellationToken ct);
}
