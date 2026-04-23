using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(
    IChampionFoundationQueryService championFoundationQueryService,
    IChampionBuildTreeQueryService championBuildTreeQueryService) : ControllerBase
{
    [HttpGet("{championId:int}")]
    public async Task<ActionResult<ChampionReadModel>> GetChampionAsync(
        int championId,
        [FromQuery] Guid? riotAccountId,
        [FromQuery] string? patch,
        [FromQuery] string? platformId,
        [FromQuery] string? position,
        [FromQuery] int maxDepth = 7,
        [FromQuery] int minBranchGames = 1,
        CancellationToken ct = default)
    {
        patch = NullIfEmpty(patch);
        platformId = NullIfEmpty(platformId);
        position = NullIfEmpty(position);

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

        var effectivePatch = NullIfEmpty(patch ?? foundationReadModel.Summary.LatestPatchVersion);
        var effectivePosition = NullIfEmpty(position ?? foundationReadModel.Summary.Position);

        var buildTreeReadModel = await championBuildTreeQueryService.GetAsync(
            championId,
            riotAccountId,
            effectivePatch,
            platformId,
            effectivePosition,
            maxDepth,
            minBranchGames,
            ct);

        return Ok(new ChampionReadModel
        {
            Summary = foundationReadModel.Summary,
            Core = foundationReadModel.Core,
            Advanced = foundationReadModel.Advanced,
            BuildTree = buildTreeReadModel
        });
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
