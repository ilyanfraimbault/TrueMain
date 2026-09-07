using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Http;
using TrueMain.Services.Champions.Builds;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The situational build context (#1450, read surface #1451), on its own controller because it
/// shares no parameter with its neighbours — no rank, no population, no opponent — and reads a
/// table none of them touch. The route prefix is the same, so the endpoint sits where a caller
/// expects it. This was the first controller split out of the champion surface; #1520
/// generalised the rule it set.
/// </summary>
public sealed class ChampionItemContextController(
    IChampionItemContextQueryService itemContextQueryService) : ChampionsControllerBase
{
    /// <summary>
    /// The situational build context of a champion at a position (#1450): for every item
    /// its builds reach, whether it is core, situational or a preference, and — when
    /// situational — the draft situations that measurably move it, each with its rates and
    /// its sample.
    ///
    /// <para>
    /// Deliberately narrower than its neighbours. There is no <c>eloBracket</c> and no
    /// <c>truemainsOnly</c>: the verdicts carry no rank dimension (a situation is far rarer
    /// than a champion, and splitting the games eleven ways starves the buckets the whole
    /// feature rests on), so either parameter would be a filter that silently does nothing.
    /// The response carries <c>allRanks</c> so the client can say so instead. There is no
    /// <c>opponentChampionId</c> either: a matchup answers a different question from a
    /// situation, and the verdicts are not folded per opponent.
    /// </para>
    ///
    /// <para>
    /// <paramref name="patch"/> is optional — omitted, the newest patch this champion has
    /// verdicts for is served, so a page whose patch filter has not settled still gets an
    /// answer. A slice with no verdicts yet returns an empty list, not a 404: nothing
    /// measured is a state, not an error.
    /// </para>
    /// </summary>
    [HttpGet("{championId:int}/item-context")]
    [ProducesResponseType(typeof(ChampionItemContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionItemContextResponse>> GetChampionItemContextAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var response = await itemContextQueryService.GetAsync(
            championId,
            normalizedPosition,
            PatchParameter.Normalize(patch),
            ct);

        return Ok(response);
    }
}
