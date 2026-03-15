using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TrueMain.Configuration;
using TrueMain.Mapping.Ops;
using TrueMain.Services.Ops;

namespace TrueMain.Controllers.Ops;

[ApiController]
[Route("ops")]
public sealed class OpsController(
    IPipelineHealthQueryService pipelineHealthQueryService,
    IOptions<OpsOptions> opsOptions) : ControllerBase
{
    private const string OpsApiKeyHeaderName = "X-Ops-Key";

    [HttpGet("pipeline-health")]
    public async Task<ActionResult> GetPipelineHealthAsync(CancellationToken ct)
    {
        var configuredApiKey = opsOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return NotFound();
        }

        if (!Request.Headers.TryGetValue(OpsApiKeyHeaderName, out var providedApiKey)
            || !string.Equals(providedApiKey.ToString(), configuredApiKey, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        var readModel = await pipelineHealthQueryService.GetAsync(ct);
        return Ok(readModel.ToContract());
    }
}
