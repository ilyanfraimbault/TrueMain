using Data.Entities;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The known-players intake (#2016): a scraper sends the people public sources list, with their
/// links and Riot accounts, and the API links each account to the account TrueMain tracks. The
/// second ops controller that writes, after the accounts one. Also the operator's queue of the
/// accounts two personas claim (#2253), and the actions that settle them.
/// </summary>
public sealed class OpsPersonasController(
    IPersonaUpsertService personaUpsertService,
    IPersonaConflictQueryService conflictQueryService,
    IPersonaConflictResolutionService conflictResolutionService) : OpsControllerBase
{
    /// <summary>
    /// Upserts up to <see cref="PersonaBatchRequest.BatchLimit"/> personas, each keyed on its slug,
    /// and answers one result per persona in request order. A malformed persona is reported in its
    /// result and skipped, never failing the batch; 400 only for a batch over the limit.
    /// </summary>
    [HttpPost("personas/batch")]
    [ProducesResponseType(typeof(PersonaBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaBatchResponse>> UpsertBatchAsync(
        [FromBody] PersonaBatchRequest request,
        CancellationToken ct = default)
    {
        var entries = request.Personas ?? [];
        if (entries.Count > PersonaBatchRequest.BatchLimit)
        {
            return ValidationProblem(
                $"personas holds {entries.Count} entries; at most "
                + $"{PersonaBatchRequest.BatchLimit} per request. Send several batches.");
        }

        return Ok(new PersonaBatchResponse { Personas = await personaUpsertService.UpsertBatchAsync(entries, ct) });
    }

    /// <summary>
    /// Accounts two personas claim, most recently seen first, paged. Every filter is optional; an
    /// unknown status, source or region is a 400 rather than an empty page.
    /// </summary>
    /// <param name="status"><c>Open</c> or <c>Resolved</c>; omit for both.</param>
    /// <param name="source">The source of the claim (<c>Dpm</c>, <c>Leaguepedia</c>, …).</param>
    /// <param name="region">A platform id (<c>EUW1</c>).</param>
    /// <param name="search">Substring of either persona's slug or name, or of the Riot ID.</param>
    /// <param name="page">1-based page (clamped to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (clamped to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("personas/conflicts")]
    [ProducesResponseType(typeof(PersonaConflictsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PersonaConflictsReadModel>> GetConflictsAsync(
        [FromQuery] string? status,
        [FromQuery] string? source,
        [FromQuery] string? region,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default)
    {
        if (!TryParseOptional<PersonaConflictStatus>(status, out var statusFilter))
        {
            return ValidationProblem($"status '{status}' is not one of {string.Join(", ", Enum.GetNames<PersonaConflictStatus>())}.");
        }

        if (!TryParseOptional<PersonaSource>(source, out var sourceFilter))
        {
            return ValidationProblem($"source '{source}' is not one of {string.Join(", ", Enum.GetNames<PersonaSource>())}.");
        }

        if (!this.TryNormalizeOptionalPlatform(region, nameof(region), out var platformId, out var regionProblem))
        {
            return regionProblem;
        }

        var filter = new PersonaConflictFilter(statusFilter, sourceFilter, platformId, search);
        return Ok(await conflictQueryService.GetPageAsync(filter, page, pageSize, ct));
    }

    /// <summary>One conflict with both personas in full — every account and link, with its source.</summary>
    [HttpGet("personas/conflicts/{id:guid}")]
    [ProducesResponseType(typeof(PersonaConflictDetailReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonaConflictDetailReadModel>> GetConflictAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await conflictQueryService.GetAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// Settles an open conflict: <c>move</c> the account to the claiming persona, <c>keep</c> it with
    /// its holder, <c>detach</c> it from both, or <c>merge</c> the two personas (<c>keep</c>: which one
    /// stays). The decision holds against the scrapers' next runs. 409 when the conflict is already
    /// resolved or the account changed hands since.
    /// </summary>
    [HttpPost("personas/conflicts/{id:guid}/resolve")]
    [ProducesResponseType(typeof(PersonaConflictResolvedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonaConflictResolvedResponse>> ResolveConflictAsync(
        Guid id,
        [FromBody] PersonaConflictResolveRequest request,
        CancellationToken ct = default)
    {
        var outcome = await conflictResolutionService.ResolveAsync(id, request, ct);
        return outcome.Result switch
        {
            PersonaConflictResolveResult.Ok => Ok(outcome.Resolved),
            PersonaConflictResolveResult.Invalid => ValidationProblem(outcome.Error),
            PersonaConflictResolveResult.NotOpen => Problem(outcome.Error, statusCode: StatusCodes.Status409Conflict),
            _ => NotFound(),
        };
    }

    internal static bool TryParseOptional<TEnum>(string? raw, out TEnum? value)
        where TEnum : struct, Enum
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (Enum.TryParse<TEnum>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
