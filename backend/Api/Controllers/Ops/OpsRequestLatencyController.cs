using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Diagnostics;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// How fast the ops API itself answers (#2307): the latency the Api measured on every ops
/// route, so a slow admin read shows up on a panel instead of being found by hand.
/// </summary>
public sealed class OpsRequestLatencyController(IRequestLatencyQueryService queryService) : OpsControllerBase
{
    /// <summary>
    /// Per ops route over the window: request count, p50/p95 (estimated from fixed buckets),
    /// mean, max, timeouts and 5xx, and a p95 sparkline — slowest p95 first.
    /// </summary>
    /// <param name="window">Relative window: <c>24h</c> (default) or <c>7d</c>.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("request-latency")]
    [ProducesResponseType(typeof(RequestLatencyReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RequestLatencyReadModel>> GetRequestLatencyAsync(
        [FromQuery] string? window,
        CancellationToken ct = default)
        => Ok(await queryService.GetAsync(window, ct));
}
