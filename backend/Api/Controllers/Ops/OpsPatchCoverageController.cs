using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Coverage;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// Whether the aggregates cover the patch the site is serving: per-scope coverage against the
/// sample floors the champion directory applies, so an operator can tell "no data yet" apart
/// from "a fold is stuck".
/// </summary>
public sealed class OpsPatchCoverageController(
    IPatchCoverageQueryService patchCoverageQueryService) : OpsControllerBase
{
    /// <summary>
    /// Whether the patches the public surfaces read are actually servable (#1033): per
    /// patch, matches and participants ingested by game date, how many
    /// <c>(champion, lane)</c> lines have an aggregate at all and how many clear the
    /// games floor the champion directory reads with, which lines are still below it, and
    /// each fold's coverage and freshness on that patch.
    ///
    /// <para>
    /// Its own endpoint rather than a card on the aggregation panel because it is a set of
    /// grouped scans over tables that carry no index on their patch column — affordable
    /// behind an explicit navigation, not on a page that loads on login.
    /// </para>
    /// </summary>
    [HttpGet("patch-coverage")]
    [ProducesResponseType(typeof(PatchCoverageReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PatchCoverageReadModel>> GetPatchCoverageAsync(CancellationToken ct = default)
    {
        var readModel = await patchCoverageQueryService.GetAsync(ct);
        return Ok(readModel);
    }
}
