using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Requests.Champions;
using TrueMain.Services.Champions.Composition;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The draft-aware build: a recommendation for a (possibly partial) composition, and the games
/// that recommendation was computed from. The only two POSTs under this prefix — the input, up
/// to nine champion/position slots across both teams, is too rich for query parameters — and
/// the only two that read a draft rather than a scope.
/// </summary>
public sealed class ChampionCompositionBuildController(
    ICompositionRecommendationQueryService compositionRecommendationQueryService) : ChampionsControllerBase
{
    /// <summary>
    /// Build recommendation for a (possibly partial) draft: the player's
    /// champion (route) and position plus the known ally/enemy picks. The
    /// composition ranks historical games, it never hard-filters — a sparse
    /// draft degrades to the champion's recent games at the position and the
    /// confidence block says so. POST because the input — up to nine
    /// champion/position slots — is too rich for query parameters.
    /// </summary>
    [HttpPost("{championId:int}/composition-build")]
    [ProducesResponseType(typeof(CompositionBuildResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompositionBuildResponse>> PostCompositionBuildAsync(
        int championId,
        [FromBody] CompositionBuildRequest request,
        CancellationToken ct = default)
    {
        if (!this.TryBuildCompositionCriteria(championId, request, out var criteria, out var problem))
        {
            return problem;
        }

        return Ok(await compositionRecommendationQueryService.GetAsync(criteria, ct));
    }

    /// <summary>
    /// The games the recommendation for that same draft was computed from, one
    /// page at a time, in the selection's own order (mains first, then
    /// similarity, recency breaking ties). Same body as the recommendation
    /// itself — the draft is the identity of the selection — so the two always
    /// answer about the same sample; a separate route because the matchup page
    /// refetches the build on every draft edit and must not pay for hydrating
    /// match rows nobody opened (#940).
    /// </summary>
    [HttpPost("{championId:int}/composition-build/games")]
    [ProducesResponseType(typeof(CompositionBuildGamesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompositionBuildGamesResponse>> PostCompositionBuildGamesAsync(
        int championId,
        [FromBody] CompositionBuildRequest request,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        CancellationToken ct = default)
    {
        if (!this.TryBuildCompositionCriteria(championId, request, out var criteria, out var problem))
        {
            return problem;
        }

        return Ok(await compositionRecommendationQueryService.GetGamesAsync(criteria, page, pageSize, ct));
    }
}
