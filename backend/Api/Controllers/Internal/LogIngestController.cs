using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Authentication;
using TrueMain.LogIngest;
using TrueMain.ReadModels.Internal;
using TrueMain.Requests.Internal;

namespace TrueMain.Controllers.Internal;

/// <summary>
/// Where the public site's and the admin portal's servers report their own errors
/// (#1556), so they land in the ops logs next to the API's. Outside <c>/ops</c> on
/// purpose: the admin's ops proxy only ever forwards under <c>/ops</c>, and the public
/// site's proxy refuses <c>/internal</c>, so this route is reachable only by a caller
/// holding the ingest key, never through either frontend.
/// </summary>
[ApiController]
[Route("internal/logs")]
[Authorize(AuthenticationSchemes = LogIngestAuthenticationDefaults.Scheme)]
public sealed class LogIngestController(ILogIngestService logIngestService) : ControllerBase
{
    /// <summary>Fifty entries at their longest fit well below this; anything larger is not a log batch.</summary>
    public const long MaxBodyBytes = 1_048_576;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(typeof(LogIngestAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public ActionResult<LogIngestAcceptedResponse> Ingest([FromBody] LogIngestRequest request)
    {
        var result = logIngestService.Ingest(request);
        if (result.Errors.Count > 0)
        {
            foreach (var (key, message) in result.Errors)
            {
                ModelState.AddModelError(key, message);
            }

            return ValidationProblem(ModelState);
        }

        return Accepted(new LogIngestAcceptedResponse { Accepted = result.Accepted });
    }
}
