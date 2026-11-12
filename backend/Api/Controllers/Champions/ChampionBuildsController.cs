using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// What a champion builds at a position: the build page itself — items, runes, skill order,
/// summoners — optionally narrowed to a lane opponent. One controller because the two read the
/// same participants through the same scope, so a divergence between them would be a bug in one
/// page, not two.
/// </summary>
public sealed class ChampionBuildsController(
    IChampionBuildsQueryService buildsQueryService,
    IChampionMatchupBuildsQueryService matchupBuildsQueryService) : ChampionsControllerBase
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
        if (!this.TryParseChampionScope(
                championId,
                position,
                patch,
                eloBracket,
                PositionRule.DominantByDefault,
                out var scope,
                out var problem,
                truemainsOnly ? ChampionPopulation.Truemains : ChampionPopulation.Everyone))
        {
            return problem;
        }

        if (opponentChampionId is > 0)
        {
            if (!LaneMatchupScope.TryCreate(scope, opponentChampionId.Value, out var matchupScope, out var rejection))
            {
                return ValidationProblem(rejection);
            }

            var matchup = await matchupBuildsQueryService.GetAsync(matchupScope, ct);
            return matchup is null ? NotFound() : Ok(matchup);
        }

        var response = await buildsQueryService.GetAsync(scope, ct: ct);
        return response is null ? NotFound() : Ok(response);
    }
}
