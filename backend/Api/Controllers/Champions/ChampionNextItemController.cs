using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Requests.Champions;
using TrueMain.Services.Champions.NextItem;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The next item to complete in a live game (#1749), read by the desktop app's in-game panel.
/// </summary>
/// <remarks>
/// A POST for the same reason as the draft endpoint: the request is a whole game state — ten
/// champions, their lanes, the items held — which does not fit a query string and changes on
/// every purchase.
/// </remarks>
public sealed class ChampionNextItemController(INextItemQueryService nextItem) : ChampionsControllerBase
{
    /// <summary>
    /// Rank the items this game's champion could complete next, from where its build stands
    /// and against the game it is in.
    /// </summary>
    [HttpPost("{championId:int}/next-item")]
    [ProducesResponseType(typeof(ChampionNextItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionNextItemResponse>> PostAsync(
        int championId,
        [FromBody] NextItemRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ToCriteria() is { } criteria
            ? Ok(await nextItem.GetAsync(championId, criteria, ct))
            : this.UnknownPosition();
    }
}
