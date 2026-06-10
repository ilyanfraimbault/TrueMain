using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Authentication;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops;

namespace TrueMain.Controllers.Ops;

[ApiController]
[Route("ops")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
public sealed class OpsController(
    IPipelineHealthQueryService pipelineHealthQueryService,
    IOverviewQueryService overviewQueryService,
    IChampionStatsQueryService championStatsQueryService,
    IMatchesOverTimeQueryService matchesOverTimeQueryService,
    ITableStatsQueryService tableStatsQueryService,
    IProcessRunsQueryService processRunsQueryService,
    ILogsQueryService logsQueryService,
    ISeedRequestService seedRequestService,
    ISeedRequestQueryService seedRequestQueryService) : ControllerBase
{
    [HttpGet("pipeline-health")]
    [ProducesResponseType(typeof(PipelineHealthReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PipelineHealthReadModel>> GetPipelineHealthAsync(CancellationToken ct)
    {
        var readModel = await pipelineHealthQueryService.GetAsync(ct);
        return Ok(readModel);
    }

    [HttpGet("stats/overview")]
    [ProducesResponseType(typeof(OverviewReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OverviewReadModel>> GetOverviewAsync(CancellationToken ct)
    {
        var readModel = await overviewQueryService.GetAsync(ct);
        return Ok(readModel);
    }

    [HttpGet("stats/champions")]
    [ProducesResponseType(typeof(IReadOnlyList<ChampionStatRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChampionStatRow>>> GetChampionStatsAsync(
        [FromQuery] string? region,
        [FromQuery] string? patch,
        [FromQuery] string? position,
        [FromQuery] int? queue,
        CancellationToken ct)
    {
        var rows = await championStatsQueryService.GetAsync(region, patch, position, queue, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Matches-over-time histogram, bucketed by <em>game date</em>
    /// (<c>Match.GameStartTimeUtc</c>) at the requested <paramref name="granularity"/>
    /// and returned chronologically. For week/month/year each bucket key is the
    /// ISO-8601 UTC timestamp of the truncated period start; for patch it is the
    /// normalised "MAJOR.MINOR" version (ordered by the earliest game per patch, so
    /// it sorts chronologically rather than lexically). <paramref name="region"/> is
    /// an optional <c>PlatformId</c> filter. 400 if granularity is missing or not one
    /// of week|month|year|patch.
    /// </summary>
    [HttpGet("stats/matches-over-time")]
    [ProducesResponseType(typeof(IReadOnlyList<MatchTimeBucket>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<MatchTimeBucket>>> GetMatchesOverTimeAsync(
        [FromQuery] string? granularity,
        [FromQuery] string? region,
        CancellationToken ct)
    {
        // granularity is required and closed: parse case-insensitively against the
        // four allowed values and 400 (ProblemDetails) on anything else, so the unit
        // that the query service inlines into date_trunc can only ever be one we own.
        if (!Enum.TryParse<MatchTimeGranularity>(granularity, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            ModelState.AddModelError(
                nameof(granularity),
                "granularity is required and must be one of: week, month, year, patch.");
            return ValidationProblem(ModelState);
        }

        var rows = await matchesOverTimeQueryService.GetAsync(parsed, region, ct);
        return Ok(rows);
    }

    [HttpGet("db/tables")]
    [ProducesResponseType(typeof(IReadOnlyList<TableStatRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TableStatRow>>> GetTableStatsAsync(CancellationToken ct)
    {
        var rows = await tableStatsQueryService.GetAsync(ct);
        return Ok(rows);
    }

    [HttpGet("process-runs")]
    [ProducesResponseType(typeof(ProcessRunsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProcessRunsReadModel>> GetProcessRunsAsync(
        [FromQuery] string? processName,
        [FromQuery] string? status,
        [FromQuery] DateTime? since,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var readModel = await processRunsQueryService.GetAsync(processName, status, since, limit, ct);
        return Ok(readModel);
    }

    [HttpGet("logs")]
    [ProducesResponseType(typeof(LogsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LogsReadModel>> GetLogsAsync(
        [FromQuery] string? level,
        [FromQuery] string? category,
        [FromQuery] DateTime? since,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var readModel = await logsQueryService.GetAsync(level, category, since, search, page, pageSize, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Seeds a single account into the pipeline by its Riot ID (gameName +
    /// tagLine + platformId), instead of waiting for the ladder Discovery to
    /// surface it. Records a <c>SeedRequest</c> at <c>Pending</c> and returns 202;
    /// the Ingestor's ManualSeedProcess does the actual Riot resolution + account
    /// upsert later. Idempotent: an existing unprocessed (Pending/Resolving)
    /// request for the same Riot ID on the same platform is returned as-is rather
    /// than duplicated (still a 202). 400 for a missing name/tag or an unknown
    /// platform route.
    /// </summary>
    [HttpPost("accounts/seed")]
    [ProducesResponseType(typeof(SeedRequestAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SeedRequestAcceptedResponse>> SeedAccountAsync(
        [FromBody] SeedAccountRequest request,
        CancellationToken ct)
    {
        var result = await seedRequestService.CreateAsync(
            new SeedRequestInput(request.GameName, request.TagLine, request.PlatformId),
            ct);

        if (!result.IsValid)
        {
            return ValidationProblem(result.ValidationError!);
        }

        // 202 whether the row was freshly created or an existing unprocessed one
        // was returned (idempotency): in both cases the work is accepted and
        // pending, and the caller polls GET /ops/accounts/seed/{id} for progress.
        return Accepted(new SeedRequestAcceptedResponse { Id = result.Id, Status = result.Status });
    }

    [HttpGet("accounts/seed/{id:guid}")]
    [ProducesResponseType(typeof(SeedRequestReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeedRequestReadModel>> GetSeedRequestAsync(Guid id, CancellationToken ct)
    {
        var readModel = await seedRequestQueryService.GetByIdAsync(id, ct);
        return readModel is null ? NotFound() : Ok(readModel);
    }

    [HttpGet("accounts/seed")]
    [ProducesResponseType(typeof(IReadOnlyList<SeedRequestReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<SeedRequestReadModel>>> GetSeedRequestsAsync(
        [FromQuery] string? status,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var readModels = await seedRequestQueryService.GetRecentAsync(status, limit, ct);
        return Ok(readModels);
    }
}

/// <summary>Request body for <c>POST /ops/accounts/seed</c>.</summary>
public sealed record SeedAccountRequest
{
    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public string? PlatformId { get; init; }
}

/// <summary>202 body for an accepted seed request: the row id and its current status.</summary>
public sealed record SeedRequestAcceptedResponse
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;
}
