using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Authentication;
using TrueMain.Mapping.Ops;
using TrueMain.Services.Ops;

namespace TrueMain.Controllers.Ops;

[ApiController]
[Route("ops")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
public sealed class OpsController(IPipelineHealthQueryService pipelineHealthQueryService) : ControllerBase
{
    [HttpGet("pipeline-health")]
    public async Task<ActionResult> GetPipelineHealthAsync(CancellationToken ct)
    {
        var readModel = await pipelineHealthQueryService.GetAsync(ct);
        return Ok(readModel.ToContract());
    }
}
