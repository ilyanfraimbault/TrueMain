using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Processes;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The charted series under <c>/ops/stats</c>: champion rows, matches over time, matches
/// ingested, aggregation progress and Riot API usage. They share the bucketing vocabulary —
/// a granularity and an optional region — which is why they take the same parameter helpers.
/// </summary>
public sealed class OpsStatsController(
    IChampionStatsQueryService championStatsQueryService,
    IMatchesOverTimeQueryService matchesOverTimeQueryService,
    IMatchesIngestedQueryService matchesIngestedQueryService,
    IAggregationStatsQueryService aggregationStatsQueryService,
    IRiotApiUsageQueryService riotApiUsageQueryService) : OpsControllerBase
{
    [HttpGet("stats/champions")]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionStatRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChampionStatRow>>> GetChampionStatsAsync(
        [FromQuery] string? region,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] int? queue,
        CancellationToken ct = default)
    {
        var rows = await championStatsQueryService.GetAsync(region, patch, position, queue, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Matches-over-time histogram, bucketed by <em>game date</em>
    /// (<c>Match.GameStartTimeUtc</c>) at the requested <paramref name="granularity"/>
    /// and returned chronologically. For day/week/month/year each bucket key is the
    /// ISO-8601 UTC timestamp of the truncated period start; for patch it is the
    /// normalised "MAJOR.MINOR" version (ordered by the earliest game per patch, so
    /// it sorts chronologically rather than lexically). <paramref name="region"/> is
    /// an optional <c>PlatformId</c> filter. 400 if granularity is missing or not one
    /// of day|week|month|year|patch.
    /// </summary>
    [HttpGet("stats/matches-over-time")]
    [ProducesResponseType(typeof(IReadOnlyList<MatchTimeBucket>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<MatchTimeBucket>>> GetMatchesOverTimeAsync(
        [FromQuery] string? granularity,
        [FromQuery] string? region,
        CancellationToken ct = default)
    {
        // granularity is required and closed: parse case-insensitively against the
        // allowed values and 400 (ProblemDetails) on anything else, so the unit
        // that the query service inlines into date_trunc can only ever be one we own.
        if (!this.TryParseGranularity<MatchTimeGranularity>(granularity, out var parsed, out var problem))
        {
            return problem;
        }

        var rows = await matchesOverTimeQueryService.GetAsync(parsed, region, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Match ingestion throughput: how many matches the pipeline actually ingested
    /// per period, from the recorded <c>MatchIngestion</c> run summaries (#1025).
    /// </summary>
    /// <remarks>
    /// Not a variant of <c>stats/matches-over-time</c>, which buckets games by when
    /// they were <em>played</em> and barely moves when ingestion stalls. This one
    /// answers whether the pipeline kept up. Bounded by the <c>process_runs</c> TTL,
    /// which the response reports so the caller can state the bound rather than
    /// drawing the tail beyond it as zero ingestion.
    /// </remarks>
    [HttpGet("stats/matches-ingested")]
    [ProducesResponseType(typeof(MatchesIngestedReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MatchesIngestedReadModel>> GetMatchesIngestedAsync(
        [FromQuery] string? granularity,
        [FromQuery] int? windowDays,
        CancellationToken ct = default)
    {
        // Closed set, parsed case-insensitively, same shape as matches-over-time.
        // Narrower on purpose: patch is a property of the games rather than of when we
        // ingested them, and year cannot fill two buckets under the run retention.
        if (!this.TryParseGranularity<IngestionTimeGranularity>(granularity, out var parsed, out var problem))
        {
            return problem;
        }

        var readModel = await matchesIngestedQueryService.GetAsync(parsed, windowDays, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Aggregation pipelines snapshot for the admin Aggregation panel: per family
    /// (builds patterns, matchups, synergies, powerspikes, mains) the exact
    /// row counts of its tables, champion/patch coverage, data freshness and the
    /// latest recorded run, plus the ingestion backlogs that should read zero when
    /// aggregations are caught up.
    /// </summary>
    [HttpGet("stats/aggregations")]
    [ProducesResponseType(typeof(AggregationsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AggregationsReadModel>> GetAggregationsAsync(CancellationToken ct = default)
    {
        var readModel = await aggregationStatsQueryService.GetAsync(ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Riot API usage metrics for the admin panel (#93): call counts per endpoint,
    /// status-code breakdown, a bucketed call-volume series and the latest
    /// rate-limit header snapshot, over a relative window.
    /// </summary>
    /// <param name="window">Relative window: <c>1h</c>, <c>24h</c> (default) or <c>7d</c>.</param>
    /// <param name="endpoint">Optional exact endpoint key (e.g. <c>match-v5.match</c>) to restrict to.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("riot-usage")]
    [ProducesResponseType(typeof(RiotApiUsageReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RiotApiUsageReadModel>> GetRiotApiUsageAsync(
        [FromQuery] string? window,
        [FromQuery] string? endpoint,
        CancellationToken ct = default)
    {
        var readModel = await riotApiUsageQueryService.GetAsync(window, endpoint, ct);
        return Ok(readModel);
    }
}
