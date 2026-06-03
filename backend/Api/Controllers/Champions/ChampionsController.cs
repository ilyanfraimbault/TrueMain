using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(
    IChampionSummariesQueryService summariesQueryService,
    IChampionBuildsQueryService buildsQueryService,
    IChampionTrendQueryService trendQueryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionSummaryReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChampionSummaryReadModel>>> ListChampionsAsync(
        [FromQuery] string? patch,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var summaries = await summariesQueryService.GetAllSummariesAsync(normalizedPatch, ct);
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

    /// <summary>
    /// Winrate / pickrate evolution across the last five patches for a champion
    /// on a single position. Intentionally cross-patch — it takes no patch
    /// filter, so the directory's active patch never scopes the series. Always
    /// 200 with a (possibly empty) series so the chart can render its own "not
    /// enough data" state — a champion the directory never observed simply
    /// yields no points.
    /// </summary>
    [HttpGet("{championId:int}/trend")]
    [ProducesResponseType(typeof(ChampionTrendReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChampionTrendReadModel>> GetChampionTrendAsync(
        int championId,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        var trend = await trendQueryService.GetTrendAsync(championId, normalizedPosition, ct);
        return Ok(trend);
    }
}
