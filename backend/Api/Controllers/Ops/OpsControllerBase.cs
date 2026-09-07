using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TrueMain.Authentication;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// Shared shape of every controller under the <c>/ops</c> prefix: attribute routing, the route
/// prefix, the API-key scheme that guards all of it, and the throttling response any of them
/// can return. Abstract, so MVC never discovers it as a controller of its own.
/// </summary>
/// <remarks>
/// Carrying the authorization attribute here rather than on each controller is the point: an
/// ops endpoint added to any of these files is authenticated by construction. A new controller
/// under this prefix that forgot the attribute would otherwise expose operational data
/// unauthenticated, and nothing in a route table would show it.
/// </remarks>
[ApiController]
[Route("ops")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public abstract class OpsControllerBase : ControllerBase;
