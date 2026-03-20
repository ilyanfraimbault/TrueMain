using TrueMain.Mapping.Champions;
using TrueMain.Services.Champions;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(
    IChampionFoundationQueryService championFoundationQueryService,
    IChampionBuildTreeQueryService championBuildTreeQueryService) : ControllerBase
{
    [HttpGet("{championId:int}")]
    public async Task<ActionResult> GetFoundationAsync(int championId, CancellationToken ct)
    {
        var readModel = await championFoundationQueryService.GetAsync(championId, ct);
        if (readModel is null)
        {
            return NotFound();
        }

        return Ok(readModel.ToContract());
    }

    [HttpGet("{championId:int}/build-tree")]
    public async Task<ActionResult> GetBuildTreeAsync(
        int championId,
        [FromQuery] Guid? riotAccountId,
        [FromQuery] string? patch,
        [FromQuery] string? platformId,
        [FromQuery] string? position,
        [FromQuery] int maxDepth = 7,
        [FromQuery] int minBranchGames = 1,
        CancellationToken ct = default)
    {
        var readModel = await championBuildTreeQueryService.GetAsync(
            championId,
            riotAccountId,
            patch,
            platformId,
            position,
            maxDepth,
            minBranchGames,
            ct);

        return Ok(readModel.ToContract());
    }
}
