using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Synergies;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// Who this champion plays well beside: duo partners ranked by how far the pair's win rate
/// lands above what the two champions' individual rates already predicted, and the third picks
/// that complete an already-chosen duo. Both answer the same question one player deeper, off
/// the same expected-win-rate model.
/// </summary>
public sealed class ChampionSynergiesController(
    IChampionSynergyQueryService synergyQueryService) : ChampionsControllerBase
{
    /// <summary>
    /// Best duo partners for a champion at a position: for every teammate it has
    /// been paired with often enough, the shared game count, the pair's win rate
    /// and — the value the list is ranked by — the synergy, i.e. how far that win
    /// rate lands above or below what the two champions' individual win rates
    /// already predicted. <paramref name="position"/> is the required Riot team
    /// position and <paramref name="partnerPosition"/> an optional narrowing to a
    /// single partner lane; an unrecognised value for either is a 400. Always 200
    /// with a (possibly empty) list — a champion whose own sample is too thin for
    /// an expected win rate returns no entries rather than invented ones, and the
    /// echoed <c>minGames</c> / <c>championGames</c> say why.
    /// </summary>
    [HttpGet("{championId:int}/synergies")]
    [ProducesResponseType(typeof(ChampionSynergiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionSynergiesResponse>> GetChampionSynergiesAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? partnerPosition,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        if (!this.TryNormalizeOptionalPosition(partnerPosition, out var normalizedPartnerPosition, out var partnerProblem))
        {
            return partnerProblem;
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await synergyQueryService.GetSynergiesAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedPartnerPosition,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Third picks for an already-chosen duo: restricted to the games this champion
    /// and <paramref name="partner"/> actually played together, the teammates whose
    /// trio over- or under-performed what all three individual win rates predicted.
    /// <paramref name="position"/>, <paramref name="partner"/> and
    /// <paramref name="partnerPosition"/> are all required; an unrecognised position
    /// is a 400. Always 200 — an empty completion list is the normal answer for a
    /// duo too rarely played to split a third way, and the response carries the
    /// duo's own game count so the caller can say exactly that.
    /// </summary>
    [HttpGet("{championId:int}/synergies/trios")]
    [ProducesResponseType(typeof(ChampionTrioSynergiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionTrioSynergiesResponse>> GetChampionTrioSynergiesAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery][Range(1, int.MaxValue)] int partner,
        [FromQuery] string? partnerPosition,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        if (!this.TryRequirePosition(partnerPosition, out var normalizedPartnerPosition, out var partnerProblem))
        {
            return partnerProblem;
        }

        // One team cannot field two players in a lane, so this would silently return
        // an empty list forever. Rejecting it names the mistake instead.
        if (string.Equals(normalizedPosition, normalizedPartnerPosition, StringComparison.Ordinal))
        {
            return ValidationProblem("partnerPosition must differ from position — teammates play different lanes.");
        }

        var normalizedPatch = PatchParameter.Normalize(patch);
        if (!this.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await synergyQueryService.GetTrioSynergiesAsync(
            championId,
            normalizedPosition,
            partner,
            normalizedPartnerPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }
}
