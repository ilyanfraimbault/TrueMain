using TrueMain.Mapping.Champions;
using TrueMain.Services.Champions;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
public sealed class ChampionsController(IChampionFoundationQueryService championFoundationQueryService) : ControllerBase
{
    [HttpGet("{championId:int}")]
    public async Task<ActionResult> GetFoundationAsync(int championId, CancellationToken ct)
    {
        var readModel = await championFoundationQueryService.GetAsync(championId, ct);
        if (readModel is null)
        {
            return NotFound();
        }

        return Ok(readModel.ToContract());
    }
}
