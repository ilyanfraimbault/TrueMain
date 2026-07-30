using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// The champion page scoped to one lane opponent (#923). Returns the same
/// <see cref="ChampionResponse"/> shape as the aggregate-backed path, so selecting an
/// opponent narrows the data without changing what the page can render.
/// </summary>
public interface IChampionMatchupBuildsQueryService
{
    /// <summary>
    /// Builds, variations, core and item tree recomputed from the games where
    /// <paramref name="championId"/> faced <paramref name="opponentChampionId"/> in
    /// <paramref name="position"/>.
    /// </summary>
    /// <param name="championId">Champion the page is about.</param>
    /// <param name="opponentChampionId">Lane opponent to scope to.</param>
    /// <param name="patch">
    /// Optional <c>major.minor</c> patch. Null spans every patch still held in
    /// <c>match_participants</c> — retention keeps the two most recent.
    /// </param>
    /// <param name="position">Canonical Riot position; both sides are matched on it.</param>
    /// <param name="eloBracket">
    /// Optional elo filter, same vocabulary as the aggregate path (<c>ALL</c>, a bare
    /// tier, or a <c>TIER_PLUS</c> form).
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>
    /// Null when the retained window holds no game of this matchup — distinct from a
    /// response with zero builds, so the caller can say "no data for this matchup".
    /// </returns>
    Task<ChampionResponse?> GetAsync(
        int championId,
        int opponentChampionId,
        string? patch,
        string position,
        string? eloBracket,
        CancellationToken ct);
}
