using Core.Lol.Identifiers;
using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ladder;
using TrueMain.Services.Ladder;

namespace TrueMain.Controllers.Ladder;

[ApiController]
[Route("ladder")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class LadderController(IApexCutoffsQueryService apexCutoffsQueryService) : ControllerBase
{
    /// <summary>
    /// The region's Grandmaster and Challenger thresholds, one entry per measured UTC day
    /// (#2174): the lowest LP in each league that day, which the ranked card's LP curve draws
    /// as the line where those tiers begin. Days before the first measurement are absent.
    /// </summary>
    /// <param name="platformId">The Riot platform, e.g. <c>EUW1</c>.</param>
    /// <param name="days">The window, in days back from today (90 by default, 730 at most).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("{platformId}/apex-cutoffs")]
    [ProducesResponseType(typeof(ApexCutoffsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApexCutoffsReadModel>> GetApexCutoffsAsync(
        string platformId,
        [FromQuery] int? days,
        CancellationToken ct = default)
    {
        if (!PlatformId.TryParse(platformId, out var platform))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid platform",
                Detail = $"Unknown platform id '{platformId}'.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        return Ok(await apexCutoffsQueryService.GetAsync(platform, days ?? 0, ct));
    }
}
