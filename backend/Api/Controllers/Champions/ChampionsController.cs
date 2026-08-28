using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;
using TrueMain.Services.Truemains;

namespace TrueMain.Controllers.Champions;

[ApiController]
[Route("champions")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class ChampionsController(
    IChampionSummariesQueryService summariesQueryService,
    IChampionTierListQueryService tierListQueryService,
    IChampionOverviewQueryService overviewQueryService,
    IChampionBuildsQueryService buildsQueryService,
    IChampionMatchupBuildsQueryService matchupBuildsQueryService,
    IChampionTrendQueryService trendQueryService,
    IChampionPatchDiffQueryService patchDiffQueryService,
    IChampionMatchupQueryService matchupQueryService,
    IChampionSynergyQueryService synergyQueryService,
    IChampionScalingQueryService scalingQueryService,
    IChampionItemTimingsQueryService itemTimingsQueryService,
    IChampionRoamQueryService roamQueryService,
    IChampionPowerspikesQueryService powerspikesQueryService,
    IChampionMainsComparisonQueryService mainsComparisonQueryService,
    ICompositionRecommendationQueryService compositionRecommendationQueryService) : ControllerBase
{
    private const int DefaultOverviewLimit = 8;
    private const int MaxOverviewLimit = 20;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionSummaryReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ChampionSummaryReadModel>>> ListChampionsAsync(
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var result = await summariesQueryService.GetAllSummariesAsync(normalizedPatch, normalizedBracket, ct);
        return Ok(result.Summaries);
    }

    /// <summary>
    /// Champion meta / tier-list for a patch: <c>(champion, position)</c> rows
    /// bucketed into S/A/B/C/D by a winRate + pickRate blend, tiered
    /// independently per position. <paramref name="patch"/> defaults to the
    /// active patch; <paramref name="position"/> narrows to a single lane when
    /// set (an unrecognised position is a 400). Always 200 with a (possibly
    /// empty) set of tier groups, all metrics derived from the same aggregates
    /// the directory reads. The static route segment never collides with the
    /// <c>{championId:int}</c> route below — "tierlist" is not an int.
    /// </summary>
    [HttpGet("tierlist")]
    [ProducesResponseType(typeof(ChampionTierListReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionTierListReadModel>> GetTierListAsync(
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var tierList = await tierListQueryService.GetTierListAsync(normalizedPatch, normalizedPosition, normalizedBracket, ct);
        return Ok(tierList);
    }

    /// <summary>
    /// Homepage-sized snapshot (#972): the active patch's true "games analyzed"
    /// total (every aggregated game, not just the rows the ranked directory
    /// keeps) plus a short, pre-sorted slice of its strongest rows. Always the
    /// active patch, unfiltered — the homepage has no patch or elo picker of
    /// its own. <paramref name="limit"/> is clamped to <c>[1, 20]</c>, default
    /// 8. Reads the same cached entry as an unqualified <c>GET /champions</c>,
    /// so the two never disagree and the homepage never pays for a second
    /// aggregate computation. The static route segment never collides with the
    /// <c>{championId:int}</c> route below — "overview" is not an int.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ChampionOverviewReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChampionOverviewReadModel>> GetOverviewAsync(
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit ?? DefaultOverviewLimit, 1, MaxOverviewLimit);
        var overview = await overviewQueryService.GetOverviewAsync(clampedLimit, ct);
        return Ok(overview);
    }

    [HttpGet("{championId:int}")]
    [ProducesResponseType(typeof(ChampionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChampionResponse>> GetChampionAsync(
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] string? eloBracket,
        [FromQuery] int? opponentChampionId,
        CancellationToken ct = default)
    {
        if (!TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        // A matchup needs a position: "vs Darius" is only meaningful in a lane, and the
        // self-join matches both sides on it. Without one there is nothing to scope, so
        // the request is rejected rather than silently answered with global data.
        if (opponentChampionId is > 0)
        {
            if (string.IsNullOrEmpty(normalizedPosition))
            {
                return ValidationProblem("A matchup requires a position: pass ?position= alongside ?opponentChampionId=.");
            }

            var matchup = await matchupBuildsQueryService.GetAsync(
                championId,
                opponentChampionId.Value,
                normalizedPatch,
                normalizedPosition,
                normalizedBracket,
                ct);

            return matchup is null ? NotFound() : Ok(matchup);
        }

        var response = await buildsQueryService.GetAsync(
            championId,
            normalizedPatch,
            normalizedPosition,
            ct,
            eloBracket: normalizedBracket);

        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// Winrate / pickrate evolution across the last five patches for a champion
    /// on a single position. Intentionally cross-patch — it takes no patch
    /// filter, so the directory's active patch never scopes the series. Always
    /// 200 with a (possibly empty) series so the chart can render its own "not
    /// enough data" state — a champion the directory never observed simply
    /// yields no points.
    /// </summary>
    [HttpGet("{championId:int}/trend")]
    [ProducesResponseType(typeof(ChampionTrendReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionTrendReadModel>> GetChampionTrendAsync(
        int championId,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        if (!TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var trend = await trendQueryService.GetTrendAsync(championId, normalizedPosition, ct);
        return Ok(trend);
    }

    /// <summary>
    /// What changed for a champion between two patches (issue #534): the
    /// win-rate swing plus whether the most popular first item, keystone and
    /// skill order moved, at a single position. <paramref name="from"/> /
    /// <paramref name="to"/> are the older and newer patch; either may be
    /// omitted, in which case the service defaults to the two most recent
    /// patches with data for the resolved lane. Always 200 with a (possibly
    /// half-empty) model so the page can render its own "not enough data" state
    /// — a patch the champion was never played on simply yields a null side.
    /// </summary>
    [HttpGet("{championId:int}/patch-diff")]
    [ProducesResponseType(typeof(ChampionPatchDiffReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionPatchDiffReadModel>> GetChampionPatchDiffAsync(
        int championId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        if (!TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedFrom = ChampionQueryParameterNormalizer.NormalizePatch(from);
        var normalizedTo = ChampionQueryParameterNormalizer.NormalizePatch(to);

        var diff = await patchDiffQueryService.GetDiffAsync(
            championId, normalizedFrom, normalizedTo, normalizedPosition, ct);
        return Ok(diff);
    }

    /// <summary>
    /// Lane matchups for a champion at a position: every lane opponent it met
    /// (above the configured minimum-games floor) with its head-to-head game
    /// count, win count and win rate, computed live from
    /// <c>match_participants</c>. <paramref name="position"/> is the required
    /// Riot team position; an unrecognised position is a 400. Always 200 with a
    /// (possibly empty) list — a champion with no opponent above the floor just
    /// yields no entries. With <paramref name="opponent"/> set, only that single
    /// head-to-head is returned and the floor drops to one game (a deliberate
    /// lookup); otherwise the frontend slices the best / worst from the list.
    /// </summary>
    [HttpGet("{championId:int}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetChampionMatchupsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        [FromQuery][Range(1, int.MaxValue)] int? opponent,
        CancellationToken ct = default)
    {
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await matchupQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            riotAccountId: null,
            opponentChampionId: opponent,
            normalizedBracket,
            ct);

        return Ok(response);
    }

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
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        if (!TryNormalizeOptionalPosition(partnerPosition, out var normalizedPartnerPosition, out var partnerProblem))
        {
            return partnerProblem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
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
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        if (!TryRequirePosition(partnerPosition, out var normalizedPartnerPosition, out var partnerProblem))
        {
            return partnerProblem;
        }

        // One team cannot field two players in a lane, so this would silently return
        // an empty list forever. Rejecting it names the mistake instead.
        if (string.Equals(normalizedPosition, normalizedPartnerPosition, StringComparison.Ordinal))
        {
            return ValidationProblem("partnerPosition must differ from position — teammates play different lanes.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
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

    /// <summary>
    /// How a champion's win rate scales with game length at a position: win rate
    /// bucketed by game duration plus a single scaling index (long-game win rate
    /// minus short-game win rate; positive = scales late), computed live from
    /// match participants. <paramref name="position"/> is the required Riot team
    /// position; an unrecognised position is a 400. Always 200 with a (possibly
    /// empty) bucket list — buckets below the sample floor are dropped.
    /// </summary>
    [HttpGet("{championId:int}/scaling")]
    [ProducesResponseType(typeof(ChampionScalingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionScalingResponse>> GetChampionScalingAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await scalingQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Average first-purchase time of each item for a champion at a position — the
    /// "power spike" timeline, computed live from the participants' item-purchase
    /// events. <paramref name="position"/> is the required Riot team position; an
    /// unrecognised position is a 400. Always 200 with a (possibly empty) list
    /// ordered earliest-first; items below the sample floor are dropped. The caller
    /// classifies items (core / boots / consumable) from static item data.
    /// </summary>
    [HttpGet("{championId:int}/item-timings")]
    [ProducesResponseType(typeof(ChampionItemTimingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionItemTimingsResponse>> GetChampionItemTimingsAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await itemTimingsQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// How much a champion roams at a position: the average number of out-of-lane
    /// kill participations per game at the 5/10/15-minute marks, computed live from
    /// the stored kill positions. <paramref name="position"/> is the required Riot
    /// team position; an unrecognised position is a 400. Always 200; the per-game
    /// averages are null below the sample floor and for JUNGLE (no own lane).
    /// </summary>
    [HttpGet("{championId:int}/roam")]
    [ProducesResponseType(typeof(ChampionRoamResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionRoamResponse>> GetChampionRoamAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        CancellationToken ct = default)
    {
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await roamQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Event spikes for a champion at a position, scoped to one core build: the
    /// items that build completes and the level milestones (6/11/16), each
    /// carrying how much the champion's power curve accelerates around it.
    /// <paramref name="position"/> is the required Riot team position; an
    /// unrecognised position is a 400. <paramref name="buildFirstItemId"/> and
    /// <paramref name="buildKeystoneId"/> identify the core build the same way
    /// the builds read keys its tabs, and are both required (a 400 otherwise) —
    /// spikes are only meaningful within one build. Always 200; the events are
    /// empty until the per-minute data has accumulated.
    /// <paramref name="opponentChampionId"/> narrows the spikes to the games
    /// played against that lane opponent (#957), the same filter the build
    /// sections take as <c>?opponentChampionId=</c> on <c>GET /champions/{id}</c>.
    /// A matchup that has not been folded yet simply has no events — it is not an
    /// error, so this stays a 200 like every other thin slice here.
    /// </summary>
    [HttpGet("{championId:int}/powerspikes")]
    [ProducesResponseType(typeof(ChampionPowerspikesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionPowerspikesResponse>> GetChampionPowerspikesAsync(
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery] string? eloBracket,
        [FromQuery] int? buildFirstItemId,
        [FromQuery] int? buildKeystoneId,
        [FromQuery] int? opponentChampionId,
        CancellationToken ct = default)
    {
        if (!TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        if (buildFirstItemId is not > 0 || buildKeystoneId is not > 0)
        {
            return ValidationProblem("buildFirstItemId and buildKeystoneId are required and must be positive.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        if (!TryNormalizeEloBracket(eloBracket, out var normalizedBracket, out var bracketProblem))
        {
            return bracketProblem;
        }

        var response = await powerspikesQueryService.GetAsync(
            championId,
            normalizedPosition,
            normalizedPatch,
            normalizedBracket,
            buildFirstItemId.Value,
            buildKeystoneId.Value,
            opponentChampionId,
            ct);

        return Ok(response);
    }

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
        if (string.IsNullOrWhiteSpace(account))
        {
            return ValidationProblem("account is required — pass the Riot ID as Name#TAG.");
        }

        // Well-formedness is a client concern; whether we hold the account is
        // an answer. Validating here keeps the two apart, so the caller's
        // "we don't track this account yet" state can never fire on a typo that
        // isn't a Riot ID at all. Same parser the service resolves with, so the
        // two can't disagree on what they accept.
        if (!NameTagParser.TryParseRiotId(account, out _))
        {
            return ValidationProblem(InvalidRiotIdMessage("account"));
        }

        if (!string.IsNullOrWhiteSpace(main) && !NameTagParser.TryParseRiotId(main, out _))
        {
            return ValidationProblem(InvalidRiotIdMessage("main"));
        }

        if (!TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);

        var response = await mainsComparisonQueryService.GetAsync(
            championId,
            account,
            main,
            normalizedPosition,
            normalizedPatch,
            ct);

        return Ok(response);
    }

    /// <summary>
    /// Build recommendation for a (possibly partial) draft: the player's
    /// champion (route) and position plus the known ally/enemy picks. The
    /// composition ranks historical games, it never hard-filters — a sparse
    /// draft degrades to the champion's recent games at the position and the
    /// confidence block says so. POST because the input — up to nine
    /// champion/position slots — is too rich for query parameters.
    /// </summary>
    [HttpPost("{championId:int}/composition-build")]
    [ProducesResponseType(typeof(CompositionBuildResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompositionBuildResponse>> PostCompositionBuildAsync(
        int championId,
        [FromBody] CompositionBuildRequest request,
        CancellationToken ct = default)
    {
        if (!TryBuildCompositionCriteria(championId, request, out var criteria, out var problem))
        {
            return problem;
        }

        return Ok(await compositionRecommendationQueryService.GetAsync(criteria, ct));
    }

    /// <summary>
    /// The games the recommendation for that same draft was computed from, one
    /// page at a time, in the selection's own order (mains first, then
    /// similarity, recency breaking ties). Same body as the recommendation
    /// itself — the draft is the identity of the selection — so the two always
    /// answer about the same sample; a separate route because the matchup page
    /// refetches the build on every draft edit and must not pay for hydrating
    /// match rows nobody opened (#940).
    /// </summary>
    [HttpPost("{championId:int}/composition-build/games")]
    [ProducesResponseType(typeof(CompositionBuildGamesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompositionBuildGamesResponse>> PostCompositionBuildGamesAsync(
        int championId,
        [FromBody] CompositionBuildRequest request,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        CancellationToken ct = default)
    {
        if (!TryBuildCompositionCriteria(championId, request, out var criteria, out var problem))
        {
            return problem;
        }

        return Ok(await compositionRecommendationQueryService.GetGamesAsync(criteria, page, pageSize, ct));
    }

    /// <summary>
    /// Validates and normalises a composition draft body into search criteria.
    /// Shared by the recommendation and its provenance listing so the two can
    /// never disagree on what a draft means — or on which drafts are rejected.
    /// </summary>
    private bool TryBuildCompositionCriteria(
        int championId,
        CompositionBuildRequest request,
        out CompositionSearchCriteria criteria,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        criteria = null!;

        if (championId <= 0)
        {
            problem = ValidationProblem("championId must be a positive champion id.");
            return false;
        }

        if (!TryRequirePosition(request.Position, out var normalizedPosition, out var positionProblem))
        {
            problem = positionProblem;
            return false;
        }

        if (!TryNormalizeSlots(request.Allies, "allies", out var allies, out var slotProblem)
            || !TryNormalizeSlots(request.Enemies, "enemies", out var enemies, out slotProblem))
        {
            problem = slotProblem;
            return false;
        }

        if (!TryNormalizeEloBracket(request.EloBracket, out var normalizedBracket, out var bracketProblem))
        {
            problem = bracketProblem;
            return false;
        }

        if (allies.ContainsKey(normalizedPosition))
        {
            problem = ValidationProblem(
                "allies must not contain the player's own position — that slot is the champion of the route.");
            return false;
        }

        criteria = new CompositionSearchCriteria
        {
            ChampionId = championId,
            Position = normalizedPosition,
            Allies = allies,
            Enemies = enemies,
            Patch = ChampionQueryParameterNormalizer.NormalizePatch(request.Patch),
            EloBracket = normalizedBracket,
        };
        problem = null;
        return true;
    }

    /// <summary>
    /// 400 message for a Riot ID query parameter that isn't well-formed. Names
    /// the offending parameter so a caller passing both can tell which failed.
    /// </summary>
    private static string InvalidRiotIdMessage(string parameter)
        => $"{parameter} must be a Riot ID of the form Name#TAG "
           + $"(at most {NameTagParser.MaxRiotIdLength} characters).";

    /// <summary>
    /// Canonicalises one team's slot list into a position→champion map; any
    /// non-positive champion id, unrecognised position, or duplicated position
    /// within the team yields a 400 <paramref name="problem"/>. Null tolerated:
    /// the DTO defaults to an empty list, but an explicit <c>"allies": null</c>
    /// in the JSON body overrides that default with null at binding time.
    /// </summary>
    private bool TryNormalizeSlots(
        IReadOnlyList<CompositionSlotInput>? slots,
        string teamLabel,
        out Dictionary<string, int> byPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        byPosition = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in slots ?? [])
        {
            // A literal null entry in the JSON array binds as a null element.
            if (slot is null || slot.ChampionId <= 0)
            {
                problem = ValidationProblem($"{teamLabel} contains a slot without a positive championId.");
                return false;
            }

            var position = ChampionQueryParameterNormalizer.NormalizePosition(slot.Position);
            if (position is null)
            {
                problem = ValidationProblem(
                    $"{teamLabel}: {ChampionQueryParameterNormalizer.InvalidPositionMessage}");
                return false;
            }

            if (!byPosition.TryAdd(position, slot.ChampionId))
            {
                problem = ValidationProblem($"{teamLabel} contains two slots at position {position}.");
                return false;
            }
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Canonicalises a required <c>position</c> query parameter; a missing or
    /// unrecognised value yields a 400 <paramref name="problem"/>. Endpoints
    /// where position is optional (champion detail, trend, patch-diff, tier
    /// list) call <see cref="TryNormalizeOptionalPosition"/> instead.
    /// </summary>
    private bool TryRequirePosition(
        string? position,
        [NotNullWhen(true)] out string? normalizedPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            problem = ValidationProblem(ChampionQueryParameterNormalizer.InvalidPositionMessage);
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Canonicalises an optional <c>position</c> query parameter: a
    /// missing/blank value means "all positions" (<paramref name="normalizedPosition"/>
    /// comes back null), while a non-blank value that fails to canonicalise is
    /// a 400 <paramref name="problem"/> rather than silently falling back to
    /// "no filter".
    /// </summary>
    private bool TryNormalizeOptionalPosition(
        string? position,
        out string? normalizedPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            normalizedPosition = null;
            problem = null;
            return true;
        }

        normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            problem = ValidationProblem(ChampionQueryParameterNormalizer.InvalidPositionMessage);
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Canonicalises an optional <c>eloBracket</c> query parameter: a missing/blank
    /// value means "every bracket" (<paramref name="normalizedBracket"/> comes back
    /// null), while a non-blank value that is not a bracket is a 400
    /// <paramref name="problem"/>.
    /// </summary>
    /// <remarks>
    /// Rejected rather than ignored, because ignoring it is not the lenient option:
    /// an unrecognised bracket used to resolve to "no restriction", so
    /// <c>?eloBracket=GOLDD</c> answered with every rank's games under a Gold label
    /// (#1224). The same treatment the sibling <c>position</c> filter already gets on
    /// these routes.
    /// </remarks>
    private bool TryNormalizeEloBracket(
        string? eloBracket,
        out string? normalizedBracket,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (!ChampionQueryParameterNormalizer.TryNormalizeEloBracket(eloBracket, out normalizedBracket))
        {
            problem = ValidationProblem(ChampionQueryParameterNormalizer.InvalidEloBracketMessage);
            return false;
        }

        problem = null;
        return true;
    }
}
