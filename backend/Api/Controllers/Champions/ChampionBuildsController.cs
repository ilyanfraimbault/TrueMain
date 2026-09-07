using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Matchups;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// What a champion builds at a position: the build page itself — items, runes, skill order,
/// summoners, optionally narrowed to a lane opponent — and the item-timing curve computed from
/// the same purchase events. One controller because the two read the same participants through
/// the same scope, so a divergence between them would be a bug in one page, not two.
/// </summary>
public sealed class ChampionBuildsController(
    IChampionBuildsQueryService buildsQueryService,
    IChampionMatchupBuildsQueryService matchupBuildsQueryService,
    IChampionItemTimingsQueryService itemTimingsQueryService) : ChampionsControllerBase
{
    [HttpGet("{championId:int}")]
    [ProducesResponseType(typeof(ChampionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChampionResponse>> GetChampionAsync(
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] string? eloBracket,
        [FromQuery] int? opponentChampionId,
        [FromQuery] bool truemainsOnly = true,
        CancellationToken ct = default)
    {
        if (!this.TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        // A matchup needs a position: "vs Darius" is only meaningful in a lane, and the
        // self-join matches both sides on it. Without one there is nothing to scope, so
        // the request is rejected rather than silently answered with global data.
        if (opponentChampionId is > 0)
        {
            if (string.IsNullOrEmpty(normalizedPosition))
            {
                return ValidationProblem("A matchup requires a position: pass ?position= alongside ?opponentChampionId=.");
            }

            // Matchups are folded from their own aggregate, whose champion side is
            // mains-only (#1087) — the truemains population is the only one it has.
            // Rejected rather than answered, for the same reason the missing
            // position above is: quietly returning mains-only rows under
            // truemainsOnly=false would be a fabricated answer, not a lenient one.
            if (!truemainsOnly)
            {
                return ValidationProblem(
                    "Matchups are aggregated over truemains only: ?truemainsOnly=false cannot be combined "
                    + "with ?opponentChampionId=.");
            }

            var matchup = await matchupBuildsQueryService.GetAsync(
                championId,
                opponentChampionId.Value,
                normalizedPatch,
                normalizedPosition,
                normalizedBracket,
                ct);

            return matchup is null ? NotFound() : Ok(matchup);
        }

        var response = await buildsQueryService.GetAsync(
            championId,
            normalizedPatch,
            normalizedPosition,
            eloBracket: normalizedBracket,
            truemainsOnly: truemainsOnly,
            ct: ct);

        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// Average first-purchase time of each item for a champion at a position — the
    /// "power spike" timeline, computed live from the participants' item-purchase
    /// events. <paramref name="position"/> is the required Riot team position; an
    /// unrecognised position is a 400. Always 200 with a (possibly empty) list
    /// ordered earliest-first; items below the sample floor are dropped. The caller
    /// classifies items (core / boots / consumable) from static item data.
    /// </summary>
    [HttpGet("{championId:int}/item-timings")]
    [ProducesResponseType(typeof(ChampionItemTimingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionItemTimingsResponse>> GetChampionItemTimingsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
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

        var response = await itemTimingsQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }
}
