using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionMatchupQueryService
{
    /// <summary>
    /// Lists a champion's lane matchups at a position: for every opponent the
    /// champion shared a lane with (same <c>TeamPosition</c>, opposite
    /// <c>TeamId</c>) in the same match, the head-to-head game and win counts,
    /// plus the lane counters behind them and the Wilson bounds a caller ranks on.
    /// Read from <c>champion_matchup_stats</c> for every global slice; computed
    /// live from <c>match_participants</c> only when narrowed to one account,
    /// which the aggregate has no dimension for.
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
    /// <param name="opponentChampionId">
    /// Optional opponent narrowing. When set, only the head-to-head against this
    /// one champion is returned (a single entry, or none) and every games floor is
    /// dropped — a deliberate lookup answers with what exists, from one game up,
    /// and is not a ranked list a thin sample could distort.
    /// </param>
    /// <param name="eloBracket">
    /// Optional elo filter — an exact tier (<c>GOLD</c>) or a cumulative "X+"
    /// threshold (<c>GOLD_PLUS</c>); null / <c>ALL</c> spans every band. Narrows
    /// the champion side to games the tracked player was in that rank at.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The matchups list, ordered by win rate descending. On the leaderboard only
    /// opponents holding at least
    /// <see cref="TrueMain.Options.ChampionsListOptions.MinMatchupGames"/> games
    /// <em>and</em> at least
    /// <see cref="TrueMain.Options.ChampionsListOptions.MinMatchupPlayRate"/> of the
    /// champion's total matchup games appear; when none clears the floor the list is
    /// empty (still a 200, never 404 — the controller only 404s on an unknown
    /// player). Callers slice best / worst on
    /// <see cref="ChampionMatchupEntry.WinRateLowerBound"/> and
    /// <see cref="ChampionMatchupEntry.WinRateUpperBound"/>, not on this order.
    /// </returns>
    Task<ChampionMatchupsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        Guid? riotAccountId,
        int? opponentChampionId,
        string? eloBracket,
        CancellationToken ct);
}
