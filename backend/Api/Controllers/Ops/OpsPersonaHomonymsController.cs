using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// Known players sharing a name (#2271): the automatic merge of the conflicts between two of them,
/// and the operator's homonyms queue — same-name known players without a common account, merged
/// into one or marked as different people. Every action is audited and holds against the scrapers.
/// </summary>
public sealed class OpsPersonaHomonymsController(
    IPersonaAutoMergeService autoMergeService,
    IPersonaHomonymQueryService homonymQueryService,
    IPersonaHomonymService homonymService) : OpsControllerBase
{
    /// <summary>
    /// Settles as merges the open conflicts whose two known players have the same name, into the
    /// older of the two. The intake runs the same sweep after every batch; this one is the backlog's.
    /// </summary>
    [HttpPost("personas/conflicts/auto-merge")]
    [ProducesResponseType(typeof(PersonaAutoMergeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaAutoMergeResponse>> AutoMergeAsync(CancellationToken ct = default) =>
        Ok(await autoMergeService.MergeHomonymConflictsAsync(ct));

    /// <summary>Groups of live known players sharing a name, in name order, paged.</summary>
    /// <param name="search">Substring of a name or slug in the group.</param>
    /// <param name="page">1-based page (clamped to ≥ 1).</param>
    /// <param name="pageSize">Groups per page (clamped to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("personas/homonyms")]
    [ProducesResponseType(typeof(PersonaHomonymsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaHomonymsReadModel>> GetHomonymsAsync(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await homonymQueryService.GetPageAsync(search, page, pageSize, ct));

    /// <summary>Merges known players into the one kept: their slugs redirect to it. 409 when one is no longer live.</summary>
    [HttpPost("personas/homonyms/merge")]
    [ProducesResponseType(typeof(PersonaHomonymMergeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaHomonymMergeResponse>> MergeAsync(
        [FromBody] PersonaHomonymMergeRequest request, CancellationToken ct = default) =>
        Answer(await homonymService.MergeAsync(request, ct));

    /// <summary>Marks known players as different people: their group leaves the queue. 409 when one is no longer live.</summary>
    [HttpPost("personas/homonyms/distinct")]
    [ProducesResponseType(typeof(PersonaHomonymDistinctResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaHomonymDistinctResponse>> MarkDistinctAsync(
        [FromBody] PersonaHomonymDistinctRequest request, CancellationToken ct = default) =>
        Answer(await homonymService.MarkDistinctAsync(request, ct));

    private ActionResult<T> Answer<T>(PersonaHomonymOutcome<T> outcome)
        where T : class => outcome.Result switch
        {
            PersonaEditResult.Ok => Ok(outcome.Response),
            PersonaEditResult.Invalid => ValidationProblem(outcome.Error),
            _ => Problem(outcome.Error, statusCode: StatusCodes.Status409Conflict),
        };
}
