using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Live;

namespace TrueMain.Controllers.Truemains;

[ApiController]
[Route("truemains")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class TruemainLiveController(ILiveGameQueryService liveGameQueryService) : ControllerBase
{
    /// <summary>
    /// The profile's Live tab (#2103): the game the player is in right now, read from
    /// spectator-v5 on demand, with both teams' ranks and inferred lanes. Always 200 for a
    /// known player — <c>status</c> says in a game, not in a game, or that Riot could not be
    /// read; 404 when TrueMain holds no such account. Answers are cached 30 s per player.
    /// </summary>
    /// <param name="nameTag">The player's Riot ID as <c>Name#TAG</c> or the <c>Name-TAG</c> slug.</param>
    /// <param name="refresh">"Check now": ask Riot again unless the answer on hand is under 10 s old.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("{nameTag}/live")]
    [ProducesResponseType(typeof(LiveGameReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LiveGameReadModel>> GetLiveGameAsync(
        string nameTag,
        [FromQuery] bool refresh = false,
        CancellationToken ct = default)
    {
        var response = await liveGameQueryService.GetAsync(nameTag, refresh, ct);
        if (response is null)
        {
            return NotFound();
        }

        // Per viewer and short-lived: never let a shared cache hold someone's game.
        Response.Headers.CacheControl = "no-store";
        return Ok(response);
    }
}
