using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Progression;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// How a champion's game unfolds rather than how it ends: win rate against game length, roams
/// out of lane at the early marks, and the power spikes around item completions and level
/// milestones. All three read the timeline side of a match, and all three take the same
/// champion + position + scope, which is what makes them one controller.
/// </summary>
public sealed class ChampionProgressionController(
    IChampionScalingQueryService scalingQueryService,
    IChampionRoamQueryService roamQueryService,
    IChampionPowerspikesQueryService powerspikesQueryService) : ChampionsControllerBase
{
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
    public async Task<ActionResult<ChampionScalingResponse>> GetChampionScalingAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await scalingQueryService.GetAsync(
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
    public async Task<ActionResult<ChampionRoamResponse>> GetChampionRoamAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await roamQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Event spikes for a champion at a position, scoped to one core build: the
    /// items that build completes and the level milestones (6/11/16), each
    /// carrying how much the champion's power curve accelerates around it.
    /// <paramref name="position"/> is the required Riot team position; an
    /// unrecognised position is a 400. <paramref name="buildFirstItemId"/> and
    /// <paramref name="buildKeystoneId"/> identify the core build the same way
    /// the builds read keys its tabs, and are both required (a 400 otherwise) —
    /// spikes are only meaningful within one build. Always 200; the events are
    /// empty until the per-minute data has accumulated.
    /// <paramref name="opponentChampionId"/> narrows the spikes to the games
    /// played against that lane opponent (#957), the same filter the build
    /// sections take as <c>?opponentChampionId=</c> on <c>GET /champions/{id}</c>.
    /// A matchup that has not been folded yet simply has no events — it is not an
    /// error, so this stays a 200 like every other thin slice here.
    /// </summary>
    [HttpGet("{championId:int}/powerspikes")]
    [ProducesResponseType(typeof(ChampionPowerspikesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionPowerspikesResponse>> GetChampionPowerspikesAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        [FromQuery] int? buildFirstItemId,
        [FromQuery] int? buildKeystoneId,
        [FromQuery] int? opponentChampionId,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        if (buildFirstItemId is not > 0 || buildKeystoneId is not > 0)
        {
            return ValidationProblem("buildFirstItemId and buildKeystoneId are required and must be positive.");
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await powerspikesQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            buildFirstItemId.Value,
            buildKeystoneId.Value,
            opponentChampionId,
            ct);

        return Ok(response);
    }
}
