using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Directory;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The champion directory and the meta reads built on the same aggregates: the full listing,
/// the tier list, the homepage overview and one champion's cross-patch trend. They share a
/// population (every ranked aggregate row), a scope vocabulary (patch + elo bracket) and the
/// cached entry underneath, which is why they answer together and never disagree.
/// </summary>
public sealed class ChampionDirectoryController(
    IChampionSummariesQueryService summariesQueryService,
    IChampionTierListQueryService tierListQueryService,
    IChampionOverviewQueryService overviewQueryService,
    IChampionTrendQueryService trendQueryService) : ChampionsControllerBase
{
    private const int DefaultOverviewLimit = 8;
    private const int MaxOverviewLimit = 20;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionSummaryReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ChampionSummaryReadModel>>> ListChampionsAsync(
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        [FromQuery] bool truemainsOnly = true,
        CancellationToken ct = default)
    {
        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var result = await summariesQueryService.GetAllSummariesAsync(
            normalizedPatch, normalizedBracket, truemainsOnly, ct);
        return Ok(result.Summaries);
    }

    /// <summary>
    /// Champion meta / tier-list for a patch: <c>(champion, position)</c> rows
    /// bucketed into S/A/B/C/D by a winRate + pickRate blend, tiered
    /// independently per position. <paramref name="patch"/> defaults to the
    /// active patch; <paramref name="position"/> narrows to a single lane when
    /// set (an unrecognised position is a 400). Always 200 with a (possibly
    /// empty) set of tier groups, all metrics derived from the same aggregates
    /// the directory reads. The static route segment never collides with the
    /// <c>{championId:int}</c> route below — "tierlist" is not an int.
    /// </summary>
    [HttpGet("tierlist")]
    [ProducesResponseType(typeof(ChampionTierListReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionTierListReadModel>> GetTierListAsync(
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] string? eloBracket,
        [FromQuery] bool truemainsOnly = true,
        CancellationToken ct = default)
    {
        if (!this.TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var tierList = await tierListQueryService.GetTierListAsync(
            normalizedPatch, normalizedPosition, normalizedBracket, truemainsOnly, ct);
        return Ok(tierList);
    }

    /// <summary>
    /// Homepage-sized snapshot (#972): the active patch's true "games analyzed"
    /// total (every aggregated game, not just the rows the ranked directory
    /// keeps) plus a short, pre-sorted slice of its strongest rows. Always the
    /// active patch, unfiltered — the homepage has no patch or elo picker of
    /// its own. <paramref name="limit"/> is clamped to <c>[1, 20]</c>, default
    /// 8. Reads the same cached entry as an unqualified <c>GET /champions</c>,
    /// so the two never disagree and the homepage never pays for a second
    /// aggregate computation. The static route segment never collides with the
    /// <c>{championId:int}</c> route below — "overview" is not an int.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ChampionOverviewReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChampionOverviewReadModel>> GetOverviewAsync(
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit ?? DefaultOverviewLimit, 1, MaxOverviewLimit);
        var overview = await overviewQueryService.GetOverviewAsync(clampedLimit, ct);
        return Ok(overview);
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionTrendReadModel>> GetChampionTrendAsync(
        int championId,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        if (!this.TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var trend = await trendQueryService.GetTrendAsync(championId, normalizedPosition, ct);
        return Ok(trend);
    }
}
