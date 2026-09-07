using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The two whole-system reads the admin portal opens on: the pipeline's health verdict and the
/// counting overview beside it. Both answer "is this thing alive and how big is it" without any
/// filter, which is what separates them from the scoped reads on the other ops controllers.
/// </summary>
public sealed class OpsOverviewController(
    IPipelineHealthQueryService pipelineHealthQueryService,
    IOverviewQueryService overviewQueryService) : OpsControllerBase
{
    [HttpGet("pipeline-health")]
    [ProducesResponseType(typeof(PipelineHealthReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PipelineHealthReadModel>> GetPipelineHealthAsync(CancellationToken ct = default)
    {
        var readModel = await pipelineHealthQueryService.GetAsync(ct);
        return Ok(readModel);
    }

    [HttpGet("stats/overview")]
    [ProducesResponseType(typeof(OverviewReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OverviewReadModel>> GetOverviewAsync(CancellationToken ct = default)
    {
        var readModel = await overviewQueryService.GetAsync(ct);
        return Ok(readModel);
    }
}
