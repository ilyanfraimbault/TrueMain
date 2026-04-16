using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TrueMain.Mapping.Ops;
using TrueMain.Options;
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
            || providedApiKey.Count != 1)
        {
            return Unauthorized();
        }

        var providedApiKeyBytes = Encoding.UTF8.GetBytes(providedApiKey[0]!);
        var configuredApiKeyBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        if (!CryptographicOperations.FixedTimeEquals(providedApiKeyBytes, configuredApiKeyBytes))
        {
            return Unauthorized();
        }

        var readModel = await pipelineHealthQueryService.GetAsync(ct);
        return Ok(readModel.ToContract());
    }
}
