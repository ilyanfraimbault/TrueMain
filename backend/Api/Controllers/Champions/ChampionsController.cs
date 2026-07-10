using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(
    IChampionSummariesQueryService summariesQueryService,
    IChampionTierListQueryService tierListQueryService,
    IChampionBuildsQueryService buildsQueryService,
    IChampionTrendQueryService trendQueryService,
    IChampionPatchDiffQueryService patchDiffQueryService,
    IChampionMatchupQueryService matchupQueryService,
    IChampionTimelineLeadsQueryService timelineLeadsQueryService,
    IChampionScalingQueryService scalingQueryService,
    IChampionItemTimingsQueryService itemTimingsQueryService,
    IChampionRoamQueryService roamQueryService,
    IChampionPowerspikesQueryService powerspikesQueryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionSummaryReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChampionSummaryReadModel>>> ListChampionsAsync(
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);
        var summaries = await summariesQueryService.GetAllSummariesAsync(normalizedPatch, normalizedBracket, ct);
        return Ok(summaries);
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
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        // A blank/absent position means "all positions"; only a non-blank value
        // that fails to canonicalise is a client error.
        string? normalizedPosition = null;
        if (!string.IsNullOrWhiteSpace(position))
        {
            normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
            if (normalizedPosition is null)
            {
                return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
            }
        }

        var tierList = await tierListQueryService.GetTierListAsync(normalizedPatch, normalizedPosition, normalizedBracket, ct);
        return Ok(tierList);
    }

    [HttpGet("{championId:int}")]
    [ProducesResponseType(typeof(ChampionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionResponse>> GetChampionAsync(
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await buildsQueryService.GetAsync(
            championId,
            normalizedPatch,
            normalizedPosition,
            ct,
            eloBracket: normalizedBracket);

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
    /// What changed for a champion between two patches (issue #534): the
    /// win-rate swing plus whether the most popular first item, keystone and
    /// skill order moved, at a single position. <paramref name="from"/> /
    /// <paramref name="to"/> are the older and newer patch; either may be
    /// omitted, in which case the service defaults to the two most recent
    /// patches with data for the resolved lane. Always 200 with a (possibly
    /// half-empty) model so the page can render its own "not enough data" state
    /// — a patch the champion was never played on simply yields a null side.
    /// </summary>
    [HttpGet("{championId:int}/patch-diff")]
    [ProducesResponseType(typeof(ChampionPatchDiffReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChampionPatchDiffReadModel>> GetChampionPatchDiffAsync(
        int championId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        var normalizedFrom = ChampionQueryParameterNormalizer.NormalizePatch(from);
        var normalizedTo = ChampionQueryParameterNormalizer.NormalizePatch(to);

        var diff = await patchDiffQueryService.GetDiffAsync(
            championId, normalizedFrom, normalizedTo, normalizedPosition, ct);
        return Ok(diff);
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
        [FromQuery] string? eloBracket,
        [FromQuery][Range(1, int.MaxValue)] int? opponent,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await matchupQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            riotAccountId: null,
            opponentChampionId: opponent,
            normalizedBracket,
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
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await timelineLeadsQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// How a champion's win rate scales with game length at a position: win rate
    /// bucketed by game duration plus a single scaling index (long-game win rate
    /// minus short-game win rate; positive = scales late), computed live from
    /// match participants. <paramref name="position"/> is the required Riot team
    /// position; an unrecognised position is a 400. Always 200 with a (possibly
    /// empty) bucket list — buckets below the sample floor are dropped.
    /// </summary>
    [HttpGet("{championId:int}/scaling")]
    [ProducesResponseType(typeof(ChampionScalingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionScalingResponse>> GetChampionScalingAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await scalingQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Average first-purchase time of each item for a champion at a position — the
    /// "power spike" timeline, computed live from the participants' item-purchase
    /// events. <paramref name="position"/> is the required Riot team position; an
    /// unrecognised position is a 400. Always 200 with a (possibly empty) list
    /// ordered earliest-first; items below the sample floor are dropped. The caller
    /// classifies items (core / boots / consumable) from static item data.
    /// </summary>
    [HttpGet("{championId:int}/item-timings")]
    [ProducesResponseType(typeof(ChampionItemTimingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionItemTimingsResponse>> GetChampionItemTimingsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await itemTimingsQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// How much a champion roams at a position: the average number of out-of-lane
    /// kill participations per game at the 5/10/15-minute marks, computed live from
    /// the stored kill positions. <paramref name="position"/> is the required Riot
    /// team position; an unrecognised position is a 400. Always 200; the per-game
    /// averages are null below the sample floor and for JUNGLE (no own lane).
    /// </summary>
    [HttpGet("{championId:int}/roam")]
    [ProducesResponseType(typeof(ChampionRoamResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionRoamResponse>> GetChampionRoamAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await roamQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Power curve and event spikes for a champion at a position: the mean
    /// opponent-relative power per minute, with the completed build items and
    /// level milestones (6/11/16) marked by how much the curve accelerates
    /// around them. <paramref name="position"/> is the required Riot team
    /// position; an unrecognised position is a 400. Always 200; the curve and
    /// events are empty until the per-minute data has accumulated.
    /// </summary>
    [HttpGet("{championId:int}/powerspikes")]
    [ProducesResponseType(typeof(ChampionPowerspikesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionPowerspikesResponse>> GetChampionPowerspikesAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedBracket = ChampionQueryParameterNormalizer.NormalizeEloBracket(eloBracket);

        var response = await powerspikesQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }
}
