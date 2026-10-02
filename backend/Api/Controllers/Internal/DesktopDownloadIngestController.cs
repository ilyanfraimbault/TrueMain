using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Authentication;
using TrueMain.Controllers.Desktop;
using TrueMain.Requests.Internal;
using TrueMain.Services.Desktop;

namespace TrueMain.Controllers.Internal;

/// <summary>
/// Where the public site's download route reports each installer it redirected a visitor to
/// (#1805). Under <c>/internal</c>, behind the key the site already holds for its logs, so a
/// download can only be counted by the site that served it — never posted through the public
/// proxy, which refuses <c>/internal</c>.
/// </summary>
[ApiController]
[Route("internal/desktop/downloads")]
[Authorize(AuthenticationSchemes = LogIngestAuthenticationDefaults.Scheme)]
public sealed class DesktopDownloadIngestController(IDesktopTelemetryService telemetry) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(DesktopTelemetryController.MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostAsync([FromBody] DesktopDownloadRequest request, CancellationToken ct)
    {
        var errors = await telemetry.RecordDownloadAsync(request, ct);
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
