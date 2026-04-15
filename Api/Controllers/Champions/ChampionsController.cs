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
        patch = NullIfEmpty(patch);
        platformId = NullIfEmpty(platformId);
        position = NullIfEmpty(position);

        var requestedFoundationReadModel = await championFoundationQueryService.GetAsync(
            championId,
            riotAccountId,
            patch,
            platformId,
            position,
            ct);

        if (requestedFoundationReadModel is null)
        {
            return NotFound();
        }

        var effectivePatch = patch ?? requestedFoundationReadModel.Summary.LatestPatchVersion;
        var effectivePosition = position ?? requestedFoundationReadModel.Summary.Position;

        var buildTreeReadModel = await championBuildTreeQueryService.GetAsync(
            championId,
            riotAccountId,
            effectivePatch,
            platformId,
            effectivePosition,
            maxDepth,
            minBranchGames,
            ct);

        var coreReadModel = ChampionCoreBuilder.Build(
            requestedFoundationReadModel);

        return Ok(ChampionMapper.ToContract(requestedFoundationReadModel, coreReadModel, buildTreeReadModel));
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
