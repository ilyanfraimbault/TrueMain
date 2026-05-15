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
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionSummaryReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChampionSummaryReadModel>>> ListChampionsAsync(
        CancellationToken ct = default)
    {
        var summaries = await championFoundationQueryService.GetAllSummariesAsync(ct);
        return Ok(summaries);
    }

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
        [FromQuery] Guid? buildId,
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

        // Phase 6.3 — optional cross-dimension correlation pivot. When set
        // (e.g. ?buildId=<champion_dim_builds.Id>), the foundation Core /
        // Advanced blocks are computed from patterns matching that build
        // only, answering "given this build, what runes / skills / spells /
        // starters do players run". The build tree never pivots — it is
        // the build, the user navigates it via the response itself.
        var pivot = buildId.HasValue ? new ChampionPatternPivot(buildId) : ChampionPatternPivot.None;

        var foundationReadModel = await championFoundationQueryService.GetAsync(
            championId,
            riotAccountId,
            normalizedPatch,
            normalizedPlatform,
            normalizedPosition,
            pivot,
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
