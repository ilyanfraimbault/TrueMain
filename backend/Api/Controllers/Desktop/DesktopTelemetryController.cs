using Microsoft.AspNetCore.Mvc;
using TrueMain.Requests.Desktop;
using TrueMain.Services.Desktop;

namespace TrueMain.Controllers.Desktop;

/// <summary>
/// Where the desktop app reports how it is used (#1805), through the site's public
/// <c>/api</c> proxy like every other request it makes. Public on purpose — an installed app
/// holds no secret — and write-only: what it collects is read back under <c>/ops</c> alone.
/// </summary>
[ApiController]
[Route("desktop/telemetry")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class DesktopTelemetryController(IDesktopTelemetryService telemetry) : ControllerBase
{
    /// <summary>A batch is a few dozen counters; anything near this is not one.</summary>
    public const long MaxBodyBytes = 16_384;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> PostAsync([FromBody] DesktopTelemetryRequest request, CancellationToken ct)
    {
        var errors = await telemetry.RecordUsageAsync(request, ct);
        if (errors.Count > 0)
        {
            foreach (var (key, message) in errors)
            {
                ModelState.AddModelError(key, message);
            }

            return ValidationProblem(ModelState);
        }

        return Accepted();
    }
}
