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
    // Page-size bounds: 50 matches the directory UI's per-page count and is
    // the default; 200 is an upper bound for ad-hoc API callers (full
    // directories sit around ~500 rows so 200 keeps a worst-case payload
    // close to a third of the unbounded response we shipped before
    // pagination).
    internal const int DefaultPageSize = 50;
    internal const int MaxPageSize = 200;

    [HttpGet]
    [ProducesResponseType(typeof(ChampionSummariesPagedResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChampionSummariesPagedResponse>> ListChampionsAsync(
        [FromQuery] string? patch,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var resolvedPage = page is null or < 1 ? 1 : page.Value;
        var resolvedPageSize = pageSize switch
        {
            null or < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        };

        var paged = await summariesQueryService.GetSummariesPageAsync(
            normalizedPatch,
            resolvedPage,
            resolvedPageSize,
            ct);
        return Ok(paged);
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
