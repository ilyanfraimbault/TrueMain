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
    [ProducesResponseType(typeof(ChampionReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
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
        // Canonicalise raw query params to the exact strings stored on
        // champion_aggregate_scopes. The query services do exact-string
        // comparisons, so passing through the raw values causes silent 404s
        // for inputs like ?platformId=euw1 or ?patch=16.4.521.
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedPlatform = ChampionQueryParameterNormalizer.NormalizePlatform(platformId);
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);

        var foundationReadModel = await championFoundationQueryService.GetAsync(
            championId,
            riotAccountId,
            normalizedPatch,
            normalizedPlatform,
            normalizedPosition,
            ct);

        if (foundationReadModel is null)
        {
            return NotFound();
        }

        var effectivePatch = normalizedPatch
            ?? ChampionQueryParameterNormalizer.NormalizePatch(foundationReadModel.Summary.LatestPatchVersion);
        var effectivePosition = normalizedPosition
            ?? ChampionQueryParameterNormalizer.NormalizePosition(foundationReadModel.Summary.Position);

        var buildTreeReadModel = await championBuildTreeQueryService.GetAsync(
            championId,
            riotAccountId,
            effectivePatch,
            normalizedPlatform,
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
}
