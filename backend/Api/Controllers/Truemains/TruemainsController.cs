using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Controllers.Champions;
using TrueMain.ReadModels.Champions;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains;

namespace TrueMain.Controllers.Truemains;

[ApiController]
[Route("truemains")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class TruemainsController(
    IMatchSummariesQueryService matchSummariesQueryService,
    IMatchDetailQueryService matchDetailQueryService,
    IProfileQueryService profileQueryService,
    IPlayerChampionBuildsQueryService playerChampionBuildsQueryService,
    IPlayerChampionMatchupQueryService playerChampionMatchupQueryService,
    IPlayerChampionPerformanceQueryService playerChampionPerformanceQueryService,
    IRankHistoryQueryService rankHistoryQueryService,
    ITruemainActivityQueryService activityQueryService,
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
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LeaderboardResponse>> ListLeaderboardAsync(
        [FromQuery] LeaderboardQuery query,
        CancellationToken ct = default)
    {
        // An unknown ?sort= falls back to the default ranking rather than a 400:
        // it is a presentation preference, and a stale bookmark should still
        // render the leaderboard.
        var sort = string.Equals(query.Sort, "dedication", StringComparison.OrdinalIgnoreCase)
            ? LeaderboardSort.Dedication
            : LeaderboardSort.Rank;

        var response = await leaderboardQueryService.GetAsync(
            query.Page ?? 1,
            query.PageSize ?? 0,
            query.Region,
            query.Position,
            query.ChampionId,
            query.OtpOnly ?? false,
            sort,
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChampionResponse>> GetPlayerChampionAsync(
        string nameTag,
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        // A blank/absent position means "all positions"; only a non-blank
        // value that fails to canonicalise is a client error.
        if (!this.TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);

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
    /// computed only from this player's games on the champion. <paramref name="position"/>
    /// is required — a lane matchup is only meaningful within a lane — so a missing
    /// or unrecognised one is a 400; 404 when the account is unknown. A known player
    /// with no opponent above the per-player floor (see
    /// <c>ChampionsListOptions.MinPlayerMatchupGames</c>) gets a 200 with an empty
    /// list; <paramref name="opponent"/> narrows to a single head-to-head at a
    /// floor of one game.
    /// </summary>
    [HttpGet("{nameTag}/champions/{championId:int}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetPlayerChampionMatchupsAsync(
        string nameTag,
        int championId,
        [FromQuery] string? position,
        [FromQuery] string? patch,
        [FromQuery][Range(1, int.MaxValue)] int? opponent,
        CancellationToken ct = default)
    {
        // Position is required here, unlike the sibling player-scoped endpoints:
        // a lane matchup is only meaningful within a lane.
        if (!this.TryRequirePosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
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

    /// <summary>
    /// Player-scoped performance: TrueMain's per-match performance score
    /// aggregated over this player's most recent ranked games on the champion,
    /// with the per-component breakdown behind it. 400 for an unrecognised
    /// position; 404 when the name tag is malformed or the account is unknown.
    /// A known player with too thin a sample is a 200 carrying the counts and no
    /// averages, so the page renders an honest "not enough games yet".
    /// </summary>
    /// <param name="nameTag">The player's Riot id in <c>Name-TAG</c> route form.</param>
    /// <param name="championId">The champion to scope the sample to.</param>
    /// <param name="patch">Major.minor patch filter; omitted means every patch.</param>
    /// <param name="position">Lane filter; omitted means every lane.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("{nameTag}/champions/{championId:int}/performance")]
    [ProducesResponseType(typeof(PlayerChampionPerformanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerChampionPerformanceResponse>> GetPlayerChampionPerformanceAsync(
        string nameTag,
        int championId,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        CancellationToken ct = default)
    {
        // A blank/absent position means "all lanes"; only a non-blank value that
        // fails to canonicalise is a client error — same rule as the sibling
        // player-scoped endpoints above.
        if (!this.TryNormalizeOptionalPosition(position, out var normalizedPosition, out var problem))
        {
            return problem;
        }

        var normalizedPatch = ChampionQueryParameterNormalizer.NormalizePatch(patch);

        var response = await playerChampionPerformanceQueryService.GetAsync(
            nameTag,
            championId,
            normalizedPatch,
            normalizedPosition,
            ct);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{nameTag}/rank-history")]
    [ProducesResponseType(typeof(RankHistoryReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RankHistoryReadModel>> GetRankHistoryAsync(
        string nameTag,
        [FromQuery] int? days,
        CancellationToken ct = default)
    {
        var response = await rankHistoryQueryService.GetAsync(nameTag, days ?? 0, ct);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// Activity grid under the profile's LP curve (#927, reshaped in #1473): the
    /// player's ranked games drawn one square per UTC day, in three windows — every
    /// day of the current patch, the last seven days, and today's games one square
    /// each. 404 only when the name tag is malformed or the account is unknown.
    /// </summary>
    /// <remarks>
    /// The three windows ship in one response because they are foldings of the same
    /// participant rows, read once: switching window is a client-side toggle, which
    /// is what keeps two of them from disagreeing about the same afternoon. Every
    /// day inside a calendar window is emitted, played or not — an idle day carries
    /// <c>games: 0</c> and a <b>null</b> win rate, which is the wire-level
    /// difference between "did not queue" and "lost everything".
    /// </remarks>
    [HttpGet("{nameTag}/activity")]
    [ProducesResponseType(typeof(TruemainActivityReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TruemainActivityReadModel>> GetActivityAsync(
        string nameTag,
        CancellationToken ct = default)
    {
        var response = await activityQueryService.GetAsync(nameTag, ct);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{nameTag}/matches")]
    [ProducesResponseType(typeof(MatchSummariesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
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

    /// <summary>
    /// Full detail payload for a single match the player took part in: all 10
    /// participants with their build order, skill order, rune page and the
    /// timeline-derived laning stats. <paramref name="nameTag"/> scopes the
    /// route (the account must have played the match) but the response covers
    /// every participant. 404 when the name tag is malformed, the account is
    /// unknown, or the match id is not one this account played in.
    /// </summary>
    [HttpGet("{nameTag}/matches/{matchId}")]
    [ProducesResponseType(typeof(MatchDetailReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MatchDetailReadModel>> GetMatchDetailAsync(
        string nameTag,
        string matchId,
        CancellationToken ct = default)
    {
        var response = await matchDetailQueryService.GetAsync(nameTag, matchId, ct);
        return response is null ? NotFound() : Ok(response);
    }
}

/// <summary>
/// Query parameters for <c>GET /truemains</c>. <see cref="Page"/> and
/// <see cref="PageSize"/> carry <see cref="RangeAttribute"/>s so
/// <c>[ApiController]</c> rejects an out-of-range value with a 400
/// ProblemDetails at binding time, instead of the service silently clamping
/// it.
/// </summary>
public sealed record LeaderboardQuery
{
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, 50)]
    public int? PageSize { get; init; }

    public string? Region { get; init; }

    public string? Position { get; init; }

    public int? ChampionId { get; init; }

    public bool? OtpOnly { get; init; }

    /// <summary>
    /// Ranking column: <c>dedication</c> ranks by TrueMain's dedication score,
    /// anything else (including omitted) keeps the default ranked-standing
    /// order.
    /// </summary>
    public string? Sort { get; init; }
}
