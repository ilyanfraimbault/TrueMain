using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Requests.Champions;
using TrueMain.Services.Champions.Draft;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The draft assistant the desktop companion app reads (#1675).
/// </summary>
/// <remarks>
/// A POST rather than a GET: the request carries a whole champion-select state
/// — two teams, bans, pinned lanes, a candidate pool — which does not fit a
/// query string, and it changes on every pick so there is nothing to bookmark.
/// </remarks>
public sealed class ChampionDraftController(
    IDraftRecommendationQueryService draft,
    IDraftBanQueryService bans)
    : ChampionsControllerBase
{
    /// <summary>
    /// Resolve the enemy lanes and rank the candidate picks for one draft.
    /// </summary>
    [HttpPost("draft")]
    [ProducesResponseType(typeof(DraftRecommendationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DraftRecommendationResponse>> PostAsync(
        [FromBody] DraftRequest request,
        CancellationToken ct = default)
        => request.ToCriteria() is { } criteria
            ? Ok(await draft.GetAsync(criteria, ct))
            : this.UnknownPosition();

    /// <summary>
    /// Suggest the bans for one draft: the threats to the player's declared pick,
    /// or to their pool, never a champion an ally is playing (#1906).
    /// </summary>
    [HttpPost("draft/bans")]
    [ProducesResponseType(typeof(DraftBanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DraftBanResponse>> PostBansAsync(
        [FromBody] DraftBanRequest request,
        CancellationToken ct = default)
        => request.ToCriteria() is { } criteria
            ? Ok(await bans.GetAsync(criteria, ct))
            : this.UnknownPosition();
}
