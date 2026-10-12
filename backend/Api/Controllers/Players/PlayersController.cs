using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Personas;
using TrueMain.Services.Personas;

namespace TrueMain.Controllers.Players;

/// <summary>Known players — a person and every Riot account public sources list for them (#2016).</summary>
[ApiController]
[Route("players")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class PlayersController(IPersonaQueryService personaQueryService) : ControllerBase
{
    /// <summary>The person behind <paramref name="slug"/>: name, public links, accounts. 404 when unknown.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(PersonaReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonaReadModel>> GetAsync(string slug, CancellationToken ct = default)
    {
        var persona = await personaQueryService.GetAsync(slug, ct);
        if (persona is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, s-maxage=60, stale-while-revalidate=300";
        return Ok(persona);
    }
}
