using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Alerting;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// What the pipeline-health alerter did (#2292): whether it is on, which incidents it holds
/// open, and the last alerts it sent — the panel under the <c>/health</c> verdict.
/// </summary>
public sealed class OpsHealthAlertsController(IHealthAlertsQueryService queryService) : OpsControllerBase
{
    /// <summary>The alerter's settings, open incidents and newest alerts.</summary>
    /// <param name="limit">Alerts to return, newest first: 10 by default, at most 50.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("health-alerts")]
    [ProducesResponseType(typeof(HealthAlertsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HealthAlertsReadModel>> GetHealthAlertsAsync(
        [FromQuery] int? limit,
        CancellationToken ct = default)
        => Ok(await queryService.GetAsync(limit, ct));
}
