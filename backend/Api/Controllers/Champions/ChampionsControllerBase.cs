using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// Shared shape of every controller under the public <c>/champions</c> prefix: attribute
/// routing, the common route prefix, and the throttling response every one of them can
/// return. Abstract, so MVC never discovers it as a controller of its own.
/// </summary>
/// <remarks>
/// The prefix is one route family served by several controllers, split by resource rather
/// than by URL (#1520): a caller sees one <c>/champions</c> surface, while each file holds
/// the endpoints that share a question, their parameters and their services.
/// </remarks>
[ApiController]
[Route("champions")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public abstract class ChampionsControllerBase : ControllerBase;
