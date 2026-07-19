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
    /// <see cref="ChampionBuildsScope.MinGames"/> value only *prefers* a patch
    /// with enough games when resolving which to render — it no longer gates:
    /// a thinly-played champion still returns a (low-confidence) build. Only a
    /// total absence of aggregated data yields <see langword="null"/> (404).
    /// </param>
    /// <param name="eloBracket">
    /// Optional elo filter (per <c>Core.Lol.Ranking.EloBracket</c>): <c>ALL</c>,
    /// a bare tier (e.g. <c>GOLD</c> — that tier only), or a <c>TIER_PLUS</c>
    /// form (e.g. <c>GOLD_PLUS</c> — that tier and above). When null or
    /// <c>ALL</c> the response spans every tier; otherwise it recomputes the
    /// builds / skill order / win rate from the selected tier(s) only and
    /// reports its <see cref="ChampionResponse.EloCoverage"/> /
    /// <see cref="ChampionResponse.MinSampleMet"/>.
    /// </param>
    Task<ChampionResponse?> GetAsync(
        int championId,
        string? patch,
        string? position,
        CancellationToken ct,
        ChampionBuildsScope? scope = null,
        string? eloBracket = null);
}
