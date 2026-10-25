using Data.Entities;
using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The operator's management of known players (#2019): the admin list and edit panel, manual add
/// and edit (source <c>manual</c>), unlinking an account, removing a link, and removing a persona
/// — optionally as a takedown. Every edit holds against the scrapers' next
/// <c>POST /ops/personas/batch</c> and is audited.
/// </summary>
public sealed class OpsPersonaEditsController(
    IPersonaAdminQueryService queryService,
    IPersonaEditService editService,
    IPersonaRemovalService removalService) : OpsControllerBase
{
    /// <summary>Known players, most recently updated first, paged.</summary>
    /// <param name="view"><c>All</c> (default: live ones), <c>Conflicts</c>, <c>Unresolved</c> or <c>Removed</c>.</param>
    /// <param name="source">The source that first published the persona.</param>
    /// <param name="search">Substring of the slug, the name or a listed Riot ID's game name.</param>
    /// <param name="page">1-based page (clamped to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (clamped to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("personas")]
    [ProducesResponseType(typeof(PersonasPageReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonasPageReadModel>> GetPersonasAsync(
        [FromQuery] string? view,
        [FromQuery] string? source,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default)
    {
        if (!OpsPersonasController.TryParseOptional<PersonaListView>(view, out var viewFilter))
        {
            return ValidationProblem($"view '{view}' is not one of {string.Join(", ", Enum.GetNames<PersonaListView>())}.");
        }

        if (!OpsPersonasController.TryParseOptional<PersonaSource>(source, out var sourceFilter))
        {
            return ValidationProblem($"source '{source}' is not one of {string.Join(", ", Enum.GetNames<PersonaSource>())}.");
        }

        var filter = new PersonaListFilter(viewFilter ?? PersonaListView.All, sourceFilter, search);
        return Ok(await queryService.GetPageAsync(filter, page, pageSize, ct));
    }

    /// <summary>One known player in full: accounts and links with their source, exclusions, open conflicts.</summary>
    [HttpGet("personas/{id:guid}")]
    [ProducesResponseType(typeof(PersonaAdminDetailReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonaAdminDetailReadModel>> GetPersonaAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await queryService.GetAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// Adds a known player by hand. Accounts go through the intake's rules (resolved, seeded, or
    /// recorded as a conflict) and are answered one by one. 409 when the slug was ever used.
    /// </summary>
    [HttpPost("personas")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> CreateAsync(
        [FromBody] PersonaCreateRequest request, CancellationToken ct = default)
    {
        var outcome = await editService.CreateAsync(request, ct);
        return outcome.Result == PersonaEditResult.Ok
            ? StatusCode(StatusCodes.Status201Created, outcome.Edited)
            : Answer(outcome);
    }

    /// <summary>Renames a known player; the sources no longer overwrite the name.</summary>
    [HttpPatch("personas/{id:guid}")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> RenameAsync(
        Guid id, [FromBody] PersonaRenameRequest request, CancellationToken ct = default) =>
        Answer(await editService.RenameAsync(id, request, ct));

    /// <summary>
    /// Removes a known player: no page, and the scrapers cannot recreate it. <paramref name="takedown"/>
    /// also bars its accounts from every other persona.
    /// </summary>
    [HttpDelete("personas/{id:guid}")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> RemoveAsync(
        Guid id, [FromQuery] bool takedown = false, CancellationToken ct = default) =>
        Answer(await removalService.RemoveAsync(id, takedown, ct));

    /// <summary>Adds a link by hand (source <c>manual</c>). 409 when the persona already lists it.</summary>
    [HttpPost("personas/{id:guid}/links")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> AddLinkAsync(
        Guid id, [FromBody] PersonaLinkEntry request, CancellationToken ct = default) =>
        Answer(await editService.AddLinkAsync(id, request, ct));

    /// <summary>Removes a link; the sources can no longer add it back.</summary>
    [HttpDelete("personas/{id:guid}/links/{linkId:guid}")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> RemoveLinkAsync(
        Guid id, Guid linkId, CancellationToken ct = default) =>
        Answer(await editService.RemoveLinkAsync(id, linkId, ct));

    /// <summary>
    /// Adds an account by hand, answered with its intake status (<c>linked</c>, <c>seeded</c>,
    /// <c>conflict</c>, <c>excluded</c> by a takedown, …).
    /// </summary>
    [HttpPost("personas/{id:guid}/accounts")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> AddAccountAsync(
        Guid id, [FromBody] PersonaAccountEntry request, CancellationToken ct = default) =>
        Answer(await editService.AddAccountAsync(id, request, ct));

    /// <summary>Unlinks an account; the persona's sources can no longer link it back.</summary>
    [HttpDelete("personas/{id:guid}/accounts/{accountId:guid}")]
    [ProducesResponseType(typeof(PersonaEditedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaEditedResponse>> UnlinkAccountAsync(
        Guid id, Guid accountId, CancellationToken ct = default) =>
        Answer(await editService.UnlinkAccountAsync(id, accountId, ct));

    private ActionResult<PersonaEditedResponse> Answer(PersonaEditOutcome outcome) => outcome.Result switch
    {
        PersonaEditResult.Ok => Ok(outcome.Edited),
        PersonaEditResult.Invalid => ValidationProblem(outcome.Error),
        PersonaEditResult.Conflict => Problem(outcome.Error, statusCode: StatusCodes.Status409Conflict),
        _ => NotFound(),
    };
}
