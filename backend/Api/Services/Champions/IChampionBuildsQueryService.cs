using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionBuildsQueryService
{
    /// <summary>
    /// Returns the top builds for a champion at a given patch + position,
    /// each build keyed by (first completed item, primary keystone) and
    /// carrying the four UI sections — core, variations, build tree, rune
    /// pages. <see langword="null"/> means the champion has no aggregated
    /// data on the active queue (404 territory).
    /// </summary>
    /// <param name="championId">Riot champion id to build the page for.</param>
    /// <param name="patch">
    /// Requested patch (<c>major.minor</c> or full Riot version); when null
    /// the dominant patch in the scope is resolved automatically.
    /// </param>
    /// <param name="position">
    /// Requested Riot team position; when null the dominant position is
    /// resolved automatically.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="scope">
    /// Optional player narrowing. When omitted the response aggregates the
    /// global pool; when a player scope is supplied every aggregate is
    /// computed only from that player's games on the champion. The
    /// <see cref="ChampionBuildsScope.MinGames"/> floor lets the caller treat
    /// a thinly-played champion as "not enough data" (returns
    /// <see langword="null"/>) so the page can show an empty state.
    /// </param>
    Task<ChampionResponse?> GetAsync(
        int championId,
        string? patch,
        string? position,
        CancellationToken ct,
        ChampionBuildsScope? scope = null);
}
