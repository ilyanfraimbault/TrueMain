using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Matchups;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// Lane matchups: who this champion met in lane at a position, and how it fared. A position is
/// always required here — the self-join matches both sides on it, so "vs Darius" without a lane
/// has nothing to scope.
/// </summary>
public sealed class ChampionMatchupsController(
    IChampionMatchupQueryService matchupQueryService) : ChampionsControllerBase
{
    /// <summary>
    /// Lane matchups for a champion at a position: every lane opponent it met
    /// (above the configured minimum-games floor) with its head-to-head game
    /// count, win count and win rate, computed live from
    /// <c>match_participants</c>. <paramref name="position"/> is the required
    /// Riot team position; an unrecognised position is a 400. Always 200 with a
    /// (possibly empty) list — a champion with no opponent above the floor just
    /// yields no entries. With <paramref name="opponent"/> set, only that single
    /// head-to-head is returned and the floor drops to one game (a deliberate
    /// lookup); otherwise the frontend slices the best / worst from the list.
    /// </summary>
    [HttpGet("{championId:int}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetChampionMatchupsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        [FromQuery][Range(1, int.MaxValue)] int? opponent,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await matchupQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            riotAccountId: null,
            opponentChampionId: opponent,
            normalizedBracket,
            ct);

        return Ok(response);
    }
}
