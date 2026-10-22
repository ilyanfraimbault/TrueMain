using Core.Lol.Patches;
using Data.Statics;
using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Items;

namespace TrueMain.Controllers.Items;

/// <summary>
/// The game's items as the in-game shop classes them, for the desktop kit editor's item picker.
/// The source is the game data's dump on CommunityDragon, read once per patch by the provider.
/// </summary>
[ApiController]
[Route("items")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class ItemClassesController(IItemClassProvider classes) : ControllerBase
{
    /// <summary>
    /// The shop classes of every classed item of the patch.
    /// </summary>
    /// <param name="patch">Optional, as Data Dragon names it (<c>16.20.1</c>); the newest published data when omitted.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("classes")]
    [ProducesResponseType(typeof(ItemClassesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ItemClassesResponse>> GetAsync(
        [FromQuery] string? patch,
        CancellationToken ct = default)
    {
        if (patch is not null && !PatchVersion.TryParse(patch, out _))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid patch", detail: $"'{patch}' is not a patch.");
        }

        var answer = await classes.GetClassesAsync(patch, ct);
        return Ok(new ItemClassesResponse
        {
            Patch = answer.Patch,
            Items = [.. answer.Items
                .OrderBy(item => item.Key)
                .Select(item => new ItemClassReadModel
                {
                    ItemId = item.Key,
                    Classes = [.. item.Value.Select(value => value.ToString())],
                })],
        });
    }
}
