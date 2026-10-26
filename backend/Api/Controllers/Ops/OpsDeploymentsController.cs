using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// When each backend process changed version (#2297), so a chart can say whether a change in
/// its series started with a deploy.
/// </summary>
public sealed class OpsDeploymentsController(IDeploymentsQueryService queryService) : OpsControllerBase
{
    /// <summary>
    /// The version changes of every Api and Ingestor container in the window, oldest first. A
    /// restart on the same version is not a deploy and is not listed.
    /// </summary>
    /// <param name="from">Window start (ISO 8601). Defaults to 30 days before <paramref name="to"/>.</param>
    /// <param name="to">Window end (ISO 8601). Defaults to now.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("deployments")]
    [ProducesResponseType(typeof(DeploymentsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DeploymentsReadModel>> GetDeploymentsAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (DeploymentsQueryService.Validate(from, to) is { } error)
        {
            return ValidationProblem(error);
        }

        return Ok(await queryService.GetAsync(from, to, ct));
    }
}
