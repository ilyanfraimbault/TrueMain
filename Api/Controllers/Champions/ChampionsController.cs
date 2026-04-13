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
    public async Task<ActionResult> GetChampionAsync(
        int championId,
        [FromQuery] Guid? riotAccountId,
        [FromQuery] string? patch,
        [FromQuery] string? platformId,
        [FromQuery] string? position,
        [FromQuery] int maxDepth = 7,
        [FromQuery] int minBranchGames = 1,
        CancellationToken ct = default)
    {
        var foundationReadModel = await championFoundationQueryService.GetAsync(
            championId,
            riotAccountId,
            patch,
            platformId,
            position,
            ct);
        if (foundationReadModel is null)
        {
            return NotFound();
        }

        var buildTreeReadModel = await championBuildTreeQueryService.GetAsync(
            championId,
            riotAccountId,
            foundationReadModel.Summary.LatestPatchVersion,
            platformId,
            foundationReadModel.Summary.Position,
            maxDepth,
            minBranchGames,
            ct);

        return Ok(foundationReadModel.ToContract(buildTreeReadModel));
    }
}
