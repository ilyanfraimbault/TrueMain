using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Desktop;

namespace TrueMain.Controllers.Ops;

/// <summary>The desktop app's downloads and usage (#1805), for the admin portal's Desktop app page.</summary>
public sealed class OpsDesktopController(IDesktopUsageQueryService desktopUsage) : OpsControllerBase
{
    /// <summary>
    /// Downloads and usage over the last <paramref name="windowDays"/> UTC days, today included
    /// (default 30, clamped to [1, 90]). Active installs today / 7 d / 30 d are read whatever
    /// the window.
    /// </summary>
    [HttpGet("desktop/usage")]
    [ProducesResponseType(typeof(DesktopUsageReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DesktopUsageReadModel>> GetUsageAsync(
        [FromQuery] int? windowDays,
        CancellationToken ct = default) =>
        Ok(await desktopUsage.GetAsync(windowDays, ct));
}
