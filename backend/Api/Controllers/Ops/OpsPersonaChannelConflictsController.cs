using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// Known players listing the same social channel (#2286): the conflicts no account reveals. Settled
/// with the homonyms actions — merge, or mark as different people — which make them leave the list.
/// </summary>
public sealed class OpsPersonaChannelConflictsController(IPersonaChannelConflictQueryService queryService)
    : OpsControllerBase
{
    /// <summary>Open channel conflicts, the most channels in common first, paged.</summary>
    /// <param name="search">Substring of either persona's slug or name.</param>
    /// <param name="page">1-based page (clamped to ≥ 1).</param>
    /// <param name="pageSize">Conflicts per page (clamped to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("personas/channel-conflicts")]
    [ProducesResponseType(typeof(PersonaChannelConflictsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaChannelConflictsReadModel>> GetAsync(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await queryService.GetPageAsync(search, page, pageSize, ct));
}
