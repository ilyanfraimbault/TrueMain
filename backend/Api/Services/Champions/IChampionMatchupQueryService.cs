using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionMatchupQueryService
{
    /// <summary>
    /// Lists a champion's lane matchups at a position, live from
    /// <c>match_participants</c>: for every opponent the champion shared a lane
    /// with (same <c>TeamPosition</c>, opposite <c>TeamId</c>) in the same
    /// match, the head-to-head game and win counts, filtered to the configured
    /// queue (and, for a player-scoped call, to that player's games).
    /// </summary>
    /// <param name="championId">Riot champion id whose matchups are measured.</param>
    /// <param name="position">
    /// Canonical Riot team position (<c>TOP</c> / <c>JUNGLE</c> /
    /// <c>MIDDLE</c> / <c>BOTTOM</c> / <c>UTILITY</c>) both lane sides must
    /// share. Required and already validated by the caller.
    /// </param>
    /// <param name="patch">
    /// Requested patch (<c>major.minor</c> or full Riot version); when null
    /// the slice spans every patch with data. Matched against the match's
    /// full <c>GameVersion</c>.
    /// </param>
    /// <param name="riotAccountId">
    /// Optional player narrowing. When omitted the slice aggregates the global
    /// pool; when supplied only that account's games on the champion count.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The matchups list, ordered by win rate descending. Only opponents with
    /// at least the configured minimum games
    /// (<see cref="TrueMain.Options.ChampionsListOptions.MinMatchupGames"/>)
    /// appear; when no opponent clears the floor the list is empty (still a
    /// 200, never 404 — the controller only 404s on an unknown player).
    /// </returns>
    Task<ChampionMatchupsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        Guid? riotAccountId,
        CancellationToken ct);
}
