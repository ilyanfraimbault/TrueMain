using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionMatchupQueryService
{
    /// <summary>
    /// Computes a champion's lane-matchup win rate and game count against a
    /// single opponent, live from <c>match_participants</c>: games where the
    /// champion and the opponent shared a lane (same <c>TeamPosition</c>,
    /// opposite <c>TeamId</c>) in the same match, filtered to the configured
    /// queue (and, for a player-scoped call, to that player's games).
    /// </summary>
    /// <param name="championId">Riot champion id whose matchup is measured.</param>
    /// <param name="position">
    /// Canonical Riot team position (<c>TOP</c> / <c>JUNGLE</c> /
    /// <c>MIDDLE</c> / <c>BOTTOM</c> / <c>UTILITY</c>) both lane sides must
    /// share. Required and already validated by the caller.
    /// </param>
    /// <param name="opponentChampionId">Riot champion id of the lane opponent.</param>
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
    /// The matchup slice, or <see langword="null"/> when the matchup has fewer
    /// than the configured minimum games (<see
    /// cref="TrueMain.Options.ChampionsListOptions.MinMatchupGames"/>) — the
    /// "not enough data" case the controller maps to 404.
    /// </returns>
    Task<ChampionMatchupResponse?> GetAsync(
        int championId,
        string position,
        int opponentChampionId,
        string? patch,
        Guid? riotAccountId,
        CancellationToken ct);
}
