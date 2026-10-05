using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Profiles;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// Every champion's measured damage profile (#1905), read by the desktop draft to build
/// each team's physical / magic / true damage bar locally.
/// </summary>
public sealed class ChampionDamageProfilesController(IChampionDamageProfileQueryService profiles) : ChampionsControllerBase
{
    /// <summary>
    /// The damage profile of every champion the profile snapshot resolves for the patch, its
    /// split by build archetype, and a static class for the champions it does not.
    /// </summary>
    /// <param name="patch">Optional; the newest profiled patch when omitted.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("damage-profiles")]
    [ProducesResponseType(typeof(ChampionDamageProfilesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChampionDamageProfilesResponse>> GetAsync(
        [FromQuery] string? patch,
        CancellationToken ct = default)
        => Ok(await profiles.GetAsync(patch, ct));
}
