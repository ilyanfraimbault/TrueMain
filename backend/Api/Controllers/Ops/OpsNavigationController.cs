using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Navigation;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The admin sidebar's badges (#2294): what waits for the operator and the cockpit's verdict, in
/// one light read every open admin tab repeats once a minute.
/// </summary>
public sealed class OpsNavigationController(INavSummaryQueryService navSummaryQueryService) : OpsControllerBase
{
    [HttpGet("nav-summary")]
    [ProducesResponseType(typeof(NavSummaryReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NavSummaryReadModel>> GetNavSummaryAsync(CancellationToken ct = default) =>
        Ok(await navSummaryQueryService.GetAsync(ct));
}
