using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(
    IChampionSummariesQueryService summariesQueryService,
    IChampionBuildsQueryService buildsQueryService,
    IChampionTrendQueryService trendQueryService,
    IChampionMatchupQueryService matchupQueryService,
    IChampionTimelineLeadsQueryService timelineLeadsQueryService) : ControllerBase
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

    /// <summary>
    /// Lane matchups for a champion at a position: every lane opponent it met
    /// (above the configured minimum-games floor) with its head-to-head game
    /// count, win count and win rate, computed live from
    /// <c>match_participants</c>. <paramref name="position"/> is the required
    /// Riot team position; an unrecognised position is a 400. Always 200 with a
    /// (possibly empty) list — a champion with no opponent above the floor just
    /// yields no entries. With <paramref name="opponent"/> set, only that single
    /// head-to-head is returned and the floor drops to one game (a deliberate
    /// lookup); otherwise the frontend slices the best / worst from the list.
    /// </summary>
    [HttpGet("{championId:int}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetChampionMatchupsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery][Range(1, int.MaxValue)] int? opponent,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);

        var response = await matchupQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            riotAccountId: null,
            opponentChampionId: opponent,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Average lead vs the lane opponent at each minute mark (5/10/15/20/30) for a
    /// champion at a position: gold / CS / kills / level / xp / damage diffs,
    /// averaged across games above the sample floor and computed live from the
    /// per-interval timeline snapshots. <paramref name="position"/> is the required
    /// Riot team position; an unrecognised position is a 400. Always 200 with a
    /// (possibly empty) list — intervals below the floor (or before snapshots have
    /// been ingested) simply yield no entries.
    /// </summary>
    [HttpGet("{championId:int}/timeline-leads")]
    [ProducesResponseType(typeof(ChampionTimelineLeadsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionTimelineLeadsResponse>> GetChampionTimelineLeadsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);

        var response = await timelineLeadsQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            ct);

        return Ok(response);
    }
}
