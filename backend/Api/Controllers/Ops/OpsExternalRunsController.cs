using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The runs of the ops scripts kept outside the repo — the persona scraper and the OTP seeder
/// (#2119). A script starts a run, heartbeats it with its current step and counters at least
/// once a minute, and finishes it with its report; the run then reads like an Ingestor run on
/// the admin Processes page. A script whose host dies stops heartbeating and its run reads as
/// Abandoned after two minutes. Reading goes through <c>GET /ops/process-runs</c>.
/// </summary>
public sealed class OpsExternalRunsController(IExternalProcessRunService service) : OpsControllerBase
{
    /// <summary>Starts a run of an external process. 400 for a name the API does not accept.</summary>
    [HttpPost("process-runs")]
    [ProducesResponseType(typeof(ExternalProcessRunStartedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ExternalProcessRunStartedResponse>> StartAsync(
        [FromBody] ExternalProcessRunStartRequest request,
        CancellationToken ct = default)
    {
        var outcome = await service.StartAsync(request, ct);
        return outcome.Result == ExternalProcessRunResult.Ok
            ? StatusCode(StatusCodes.Status201Created, outcome.Started)
            : ToProblem(outcome);
    }

    /// <summary>
    /// Keeps a Running run alive and, when a summary is sent, replaces it. 404 for an unknown
    /// run or one that is not an external process's; 409 once the run is finished.
    /// </summary>
    [HttpPost("process-runs/{id:guid}/heartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> HeartbeatAsync(
        Guid id,
        [FromBody] ExternalProcessRunHeartbeatRequest? request,
        CancellationToken ct = default)
    {
        var outcome = await service.HeartbeatAsync(id, request ?? new ExternalProcessRunHeartbeatRequest(), ct);
        return outcome.Result == ExternalProcessRunResult.Ok ? NoContent() : ToProblem(outcome);
    }

    /// <summary>
    /// Finishes a Running run as Success, Failed or Cancelled, with its error and final
    /// summary. 404 for an unknown or non-external run; 409 when it is already finished.
    /// </summary>
    [HttpPost("process-runs/{id:guid}/finish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> FinishAsync(
        Guid id,
        [FromBody] ExternalProcessRunFinishRequest request,
        CancellationToken ct = default)
    {
        var outcome = await service.FinishAsync(id, request, ct);
        return outcome.Result == ExternalProcessRunResult.Ok ? NoContent() : ToProblem(outcome);
    }

    private ActionResult ToProblem(ExternalProcessRunOutcome outcome) => outcome.Result switch
    {
        ExternalProcessRunResult.Invalid => ValidationProblem(outcome.Error),
        ExternalProcessRunResult.NotRunning => Problem(outcome.Error, statusCode: StatusCodes.Status409Conflict),
        _ => Problem("No run of an external process has this id.", statusCode: StatusCodes.Status404NotFound),
    };
}
