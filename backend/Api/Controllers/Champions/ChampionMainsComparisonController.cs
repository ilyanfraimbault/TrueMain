using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Mains;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The one champion read that takes a player: a Riot account measured side by side against the
/// champion's tracked mains (#528). Alone on its controller because it is the only endpoint
/// under this prefix whose input is an account rather than a scope, and the only one that has
/// to keep "malformed Riot ID" (a 400) apart from "a Riot ID we hold no row for" (a 200 with a
/// reason code).
/// </summary>
public sealed class ChampionMainsComparisonController(
    IChampionMainsComparisonQueryService mainsComparisonQueryService) : ChampionsControllerBase
{
    /// <summary>
    /// Head-to-head between a Riot account and this champion's mains (issue
    /// #528): win rate, KDA, CS/min and gold side by side over the same queue,
    /// patch and lane scope. <paramref name="account"/> is the Riot ID as a
    /// player types it (<c>Name#TAG</c>; the <c>Name-TAG</c> slug is accepted
    /// too) and is required. <paramref name="main"/> narrows the right-hand
    /// column to a single tracked account; omitted, it aggregates every tracked
    /// main of the champion.
    ///
    /// Two distinct failure modes, deliberately kept apart:
    /// <list type="bullet">
    /// <item>A Riot ID that isn't well-formed — missing, blank, no separator,
    /// an empty half, over-long — is a <b>400</b>. It is malformed input, not
    /// an answer about a player.</item>
    /// <item>A well-formed Riot ID we have no row for is a <b>200</b> carrying
    /// <c>UNKNOWN_ACCOUNT</c> (or <c>UNKNOWN_TARGET</c> for
    /// <paramref name="main"/>). The comparison only covers accounts already in
    /// our database — there is no on-demand Riot fetch — so "we don't hold this
    /// player" is a normal answer for this endpoint, not a failure.</item>
    /// </list>
    ///
    /// A sample below the configured floor comes back as
    /// <c>INSUFFICIENT_SAMPLE</c> with both columns still populated so the
    /// caller can say which side is thin.
    /// </summary>
    [HttpGet("{championId:int}/mains-comparison")]
    [ProducesResponseType(typeof(ChampionMainsComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionMainsComparisonResponse>> GetChampionMainsComparisonAsync(
        int championId,
        [FromQuery] string? account,
        [FromQuery] string? main,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        CancellationToken ct = default)
    {
        // Well-formedness is a client concern; whether we hold the account is an answer.
        // Validating here keeps the two apart, so the caller's "we don't track this account
        // yet" state can never fire on a typo that isn't a Riot ID at all. Same parser the
        // service resolves with, so the two can't disagree on what they accept.
        if (!this.TryRequireRiotId(account, nameof(account), out var accountProblem))
        {
            return accountProblem;
        }

        if (!this.TryValidateOptionalRiotId(main, nameof(main), out var mainProblem))
        {
            return mainProblem;
        }

        if (!this.TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);

        var response = await mainsComparisonQueryService.GetAsync(
            championId,
            account,
            main,
            normalizedPosition,
            normalizedPatch,
            ct);

        return Ok(response);
    }
}
