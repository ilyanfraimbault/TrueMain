using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Configuration;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The configuration actually in force across the deployed processes, each key tagged with
/// whether it is a class default or an override (#1034). Alone on its controller: it is the one
/// ops read that describes the running processes rather than the data they produced.
/// </summary>
public sealed class OpsConfigurationController(
    IEffectiveConfigurationQueryService effectiveConfigurationQueryService) : OpsControllerBase
{
    /// <summary>
    /// What every host is actually running with (#1034): the Api's own options, read live
    /// from its container, plus the Ingestor's — published to Mongo at its own boot, since
    /// the Api cannot introspect a process it does not run in. Read-only; no secret-bearing
    /// section is ever included (see <see cref="Data.Configuration.EffectiveConfigurationCatalog"/>).
    /// </summary>
    [HttpGet("configuration")]
    [ProducesResponseType(typeof(EffectiveConfigurationOverviewReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EffectiveConfigurationOverviewReadModel>> GetConfigurationAsync(
        CancellationToken ct = default)
    {
        var readModel = await effectiveConfigurationQueryService.GetAsync(ct);
        return Ok(readModel);
    }
}
