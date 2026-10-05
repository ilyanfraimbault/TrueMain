using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Benchmarks;
using TrueMain.Services.Benchmarks;

namespace TrueMain.Controllers.Benchmarks;

/// <summary>
/// Reference values the desktop overlay sets a live game against (#1912). Public and
/// parameterised by position only: the tier is chosen on the player's machine from the
/// whole answer, so nothing read from the League client is sent (#1805).
/// </summary>
[ApiController]
[Route("benchmarks")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class PaceBenchmarkController(IPaceBenchmarkQueryService paceBenchmarks) : ControllerBase
{
    /// <summary>
    /// Per tier and whole minute, the quartiles of a laner's cumulative CS and gold earned at
    /// <paramref name="position"/>, pooled over the newest patches. A minute under the sample
    /// floor carries its count without quartiles. An unrecognised position is a 400.
    /// </summary>
    [HttpGet("pace")]
    [ProducesResponseType(typeof(PaceBenchmarkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaceBenchmarkResponse>> GetPaceAsync(
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        return Ok(await paceBenchmarks.GetAsync(normalizedPosition, ct));
    }
}
