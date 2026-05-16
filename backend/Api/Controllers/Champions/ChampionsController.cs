using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(
    IChampionSummariesQueryService summariesQueryService,
    IChampionBuildsQueryService buildsQueryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionSummaryReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChampionSummaryReadModel>>> ListChampionsAsync(
        CancellationToken ct = default)
    {
        var summaries = await summariesQueryService.GetAllSummariesAsync(ct);
        return Ok(summaries);
    }

    [HttpGet("{championId:int}")]
    [ProducesResponseType(typeof(ChampionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionResponse>> GetChampionAsync(
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);

        var response = await buildsQueryService.GetAsync(
            championId,
            normalizedPatch,
            normalizedPosition,
            ct);

        return response is null ? NotFound() : Ok(response);
    }
}
