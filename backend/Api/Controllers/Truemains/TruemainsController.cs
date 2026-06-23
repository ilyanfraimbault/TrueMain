using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Controllers.Champions;
using TrueMain.ReadModels.Champions;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains;

namespace TrueMain.Controllers.Truemains;

[ApiController]
[Route("truemains")]
public sealed class TruemainsController(
    IMatchSummariesQueryService matchSummariesQueryService,
    IProfileQueryService profileQueryService,
    IPlayerChampionBuildsQueryService playerChampionBuildsQueryService,
    IPlayerChampionMatchupQueryService playerChampionMatchupQueryService,
    IRankHistoryQueryService rankHistoryQueryService,
    ITruemainsLeaderboardQueryService leaderboardQueryService,
    ISearchQueryService searchQueryService) : ControllerBase
{
    /// <summary>
    /// Name/tag lookup for the search box: returns a short, ranked list of
    /// truemains whose Riot id matches <paramref name="q"/> (case-insensitive
    /// substring on the game name; a <c>Name#TAG</c> query also narrows by
    /// tag). Always 200 with a (possibly empty) list — a too-short or no-match
    /// query is a normal empty result, not an error.
    /// </summary>
    /// <param name="q">The search term: a partial game name, or a full <c>Name#TAG</c> Riot id.</param>
    /// <param name="limit">
    /// Maximum results to return. Omitted or <c>0</c> means "use the default":
    /// the service treats <c>limit &lt;= 0</c> as its default page size — the
    /// same 0-as-sentinel convention <c>GET /truemains</c> uses for
    /// <c>pageSize</c> — and clamps values above its cap down to it.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SearchResponse>> SearchAsync(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        // An omitted limit maps to 0, which the service reads as "use the
        // default" (it clamps limit <= 0 to DefaultLimit) — same 0-as-sentinel
        // convention the leaderboard endpoint uses for pageSize.
        var response = await searchQueryService.SearchAsync(q, limit ?? 0, ct);
        return Ok(response);
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(LeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LeaderboardResponse>> ListLeaderboardAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? region,
        [FromQuery] string? position,
        [FromQuery] int? championId,
        CancellationToken ct = default)
    {
        var response = await leaderboardQueryService.GetAsync(
            page ?? 1,
            pageSize ?? 0,
            region,
            position,
            championId,
            ct);

        // Let shared caches (CDN / reverse proxy) serve the leaderboard for the
        // same window the service caches it in-memory: s-maxage mirrors the 30s
        // response TTL, and stale-while-revalidate lets an edge keep serving a
        // ~30s-stale page for another 60s while it refreshes in the background,
        // so a cache expiry never lands a request on the cold DB path. Scoped
        // to the LIST action only — the profile / matches / rank-history routes
        // are per-player and keep their default (uncached) behaviour.
        Response.Headers.CacheControl = "public, s-maxage=30, stale-while-revalidate=60";

        return Ok(response);
    }

    [HttpGet("{nameTag}/profile")]
    [ProducesResponseType(typeof(ProfileReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ProfileReadModel>> GetProfileAsync(
        string nameTag,
        CancellationToken ct = default)
    {
        var response = await profileQueryService.GetAsync(nameTag, ct);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// Player-scoped champion page: the same <see cref="ChampionResponse"/>
    /// contract as <c>GET /champions/{championId}</c>, but every aggregate is
    /// computed only from this player's games on the champion. 404 when the
    /// account is unknown or the player has too few games on the champion to
    /// draw a build (see <c>PlayerChampionBuildsQueryService.MinPlayerGames</c>).
    /// </summary>
    [HttpGet("{nameTag}/champions/{championId:int}")]
    [ProducesResponseType(typeof(ChampionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionResponse>> GetPlayerChampionAsync(
        string nameTag,
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);

        var response = await playerChampionBuildsQueryService.GetAsync(
            nameTag,
            championId,
            normalizedPatch,
            normalizedPosition,
            ct);

        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// Player-scoped lane matchups: the same <see cref="ChampionMatchupsResponse"/>
    /// contract as <c>GET /champions/{championId}/matchups</c>, but every line is
    /// computed only from this player's games on the champion. 400 for an
    /// unrecognised position; 404 when the account is unknown. A known player
    /// with no opponent above the per-player floor (see
    /// <c>ChampionsListOptions.MinPlayerMatchupGames</c>) gets a 200 with an empty
    /// list; <paramref name="opponent"/> narrows to a single head-to-head at a
    /// floor of one game.
    /// </summary>
    [HttpGet("{nameTag}/champions/{championId:int}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetPlayerChampionMatchupsAsync(
        string nameTag,
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery][Range(1, int.MaxValue)] int? opponent,
        CancellationToken ct = default)
    {
        var normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            return ValidationProblem("position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.");
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);

        var response = await playerChampionMatchupQueryService.GetAsync(
            nameTag,
            championId,
            normalizedPosition,
            normalizedPatch,
            opponent,
            ct);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{nameTag}/rank-history")]
    [ProducesResponseType(typeof(RankHistoryReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RankHistoryReadModel>> GetRankHistoryAsync(
        string nameTag,
        [FromQuery] int? days,
        CancellationToken ct = default)
    {
        var response = await rankHistoryQueryService.GetAsync(nameTag, days ?? 0, ct);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{nameTag}/matches")]
    [ProducesResponseType(typeof(MatchSummariesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MatchSummariesResponse>> GetMatchesAsync(
        string nameTag,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? position,
        [FromQuery] int? championId,
        CancellationToken ct = default)
    {
        var response = await matchSummariesQueryService.GetAsync(
            nameTag,
            page ?? 1,
            pageSize ?? 0,
            position,
            championId,
            ct);

        return response is null ? NotFound() : Ok(response);
    }
}
