using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Progression;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// How a champion's game unfolds rather than how it ends: win rate against game length. Reads the
/// timeline side of a match, scoped by champion + position + scope.
/// </summary>
public sealed class ChampionProgressionController(
    IChampionScalingQueryService scalingQueryService) : ChampionsControllerBase
{
    /// <summary>
    /// How a champion's win rate scales with game length at a position: win rate
    /// bucketed by game duration plus a single scaling index (long-game win rate
    /// minus short-game win rate; positive = scales late), computed live from
    /// match participants. <paramref name="position"/> is the required Riot team
    /// position; an unrecognised position is a 400. Always 200 with a (possibly
    /// empty) bucket list — buckets below the sample floor are dropped.
    /// </summary>
    [HttpGet("{championId:int}/scaling")]
    [ProducesResponseType(typeof(ChampionScalingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionScalingResponse>> GetChampionScalingAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!this.TryParseChampionScope(
                championId, position, patch, eloBracket, PositionRule.Required, out var scope, out var problem))
        {
            return problem;
        }

        return Ok(await scalingQueryService.GetAsync(scope, ct));
    }
}
