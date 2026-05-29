using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains;

namespace TrueMain.Controllers.Truemains;

[ApiController]
[Route("truemains")]
public sealed class TruemainsController(
    IMatchSummariesQueryService matchSummariesQueryService,
    IProfileQueryService profileQueryService,
    IRankHistoryQueryService rankHistoryQueryService,
    ITruemainsLeaderboardQueryService leaderboardQueryService) : ControllerBase
{
    [HttpGet("")]
    [ProducesResponseType(typeof(LeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LeaderboardResponse>> ListLeaderboardAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? region,
        [FromQuery] string? position,
        [FromQuery] int? championId,
        CancellationToken ct = default)
    {
        var response = await leaderboardQueryService.GetAsync(
            page ?? 1,
            pageSize ?? 0,
            region,
            position,
            championId,
            ct);

        return Ok(response);
    }

    [HttpGet("{nameTag}/profile")]
    [ProducesResponseType(typeof(ProfileReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ProfileReadModel>> GetProfileAsync(
        string nameTag,
        CancellationToken ct = default)
    {
        var response = await profileQueryService.GetAsync(nameTag, ct);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{nameTag}/rank-history")]
    [ProducesResponseType(typeof(RankHistoryReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RankHistoryReadModel>> GetRankHistoryAsync(
        string nameTag,
        [FromQuery] int? days,
        CancellationToken ct = default)
    {
        var response = await rankHistoryQueryService.GetAsync(nameTag, days ?? 0, ct);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{nameTag}/matches")]
    [ProducesResponseType(typeof(MatchSummariesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MatchSummariesResponse>> GetMatchesAsync(
        string nameTag,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? position,
        [FromQuery] int? championId,
        CancellationToken ct = default)
    {
        var response = await matchSummariesQueryService.GetAsync(
            nameTag,
            page ?? 1,
            pageSize ?? 0,
            position,
            championId,
            ct);

        return response is null ? NotFound() : Ok(response);
    }
}
