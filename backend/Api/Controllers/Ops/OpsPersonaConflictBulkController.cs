using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The bulk resolution of the most certain conflicts (#2295): the preview at a score floor, then the
/// merge of exactly the conflicts the operator confirmed, each into the older known player and
/// audited like a single resolution.
/// </summary>
public sealed class OpsPersonaConflictBulkController(
    IPersonaConflictQueryService conflictQueryService,
    IPersonaConflictBulkResolutionService bulkResolutionService) : OpsControllerBase
{
    /// <summary>
    /// The open conflicts a bulk merge at <paramref name="minScore"/> would settle: a shared social
    /// channel, no differing one, a score at the floor. At most <see cref="PersonaConflictBulk.BatchLimit"/>.
    /// </summary>
    /// <param name="minScore">The score floor, at least 10 (one shared social channel).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("personas/conflicts/bulk-resolve/preview")]
    [ProducesResponseType(typeof(PersonaConflictBulkPreviewReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaConflictBulkPreviewReadModel>> PreviewAsync(
        [FromQuery] int? minScore, CancellationToken ct = default)
    {
        if (minScore is not { } floor || floor < PersonaConflictBulk.MinimumFloor)
        {
            return ValidationProblem($"minScore must be at least {PersonaConflictBulk.MinimumFloor} (one shared social channel).");
        }

        return Ok(await conflictQueryService.GetBulkPreviewAsync(floor, ct));
    }

    /// <summary>
    /// Merges the listed conflicts, each into its older known player, after checking again that it is
    /// open, lists no differing channel and still reaches <c>minScore</c>. One outcome per id; a
    /// conflict that no longer qualifies is skipped with its reason, never failing the batch.
    /// </summary>
    [HttpPost("personas/conflicts/bulk-resolve")]
    [ProducesResponseType(typeof(PersonaConflictBulkResolveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaConflictBulkResolveResponse>> ResolveAsync(
        [FromBody] PersonaConflictBulkResolveRequest request, CancellationToken ct = default)
    {
        var outcome = await bulkResolutionService.ResolveAsync(request, ct);
        return outcome.Response is { } response ? Ok(response) : ValidationProblem(outcome.Error);
    }
}
