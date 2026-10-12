using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The known-players intake (#2016): a scraper sends the people public sources list, with their
/// links and Riot accounts, and the API links each account to the account TrueMain tracks. The
/// second ops controller that writes, after the accounts one.
/// </summary>
public sealed class OpsPersonasController(IPersonaUpsertService personaUpsertService) : OpsControllerBase
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

        var results = new List<PersonaUpsertResult>(entries.Count);
        foreach (var entry in entries)
        {
            results.Add(await personaUpsertService.UpsertAsync(entry, ct));
        }

        return Ok(new PersonaBatchResponse { Personas = results });
    }
}
