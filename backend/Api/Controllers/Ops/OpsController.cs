using Core.Lol.Identifiers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Authentication;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops;
using TrueMain.Services.Truemains;

namespace TrueMain.Controllers.Ops;

[ApiController]
[Route("ops")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class OpsController(
    IPipelineHealthQueryService pipelineHealthQueryService,
    IOverviewQueryService overviewQueryService,
    IChampionStatsQueryService championStatsQueryService,
    IMatchesOverTimeQueryService matchesOverTimeQueryService,
    IMatchesIngestedQueryService matchesIngestedQueryService,
    ITableStatsQueryService tableStatsQueryService,
    IDbStorageHistoryQueryService dbStorageHistoryQueryService,
    IProcessRunsQueryService processRunsQueryService,
    IProcessIterationsQueryService processIterationsQueryService,
    ILogsQueryService logsQueryService,
    IRiotApiUsageQueryService riotApiUsageQueryService,
    IDataQualityQueryService dataQualityQueryService,
    IDataQualityDetectorsQueryService dataQualityDetectorsQueryService,
    ISeedRequestService seedRequestService,
    ISeedRequestQueryService seedRequestQueryService,
    ICandidateQueryService candidateQueryService,
    ICandidateFunnelQueryService candidateFunnelQueryService,
    ICandidateQueueLatencyQueryService candidateQueueLatencyQueryService,
    ICrashesQueryService crashesQueryService,
    IAccountExplorerQueryService accountExplorerQueryService,
    IAggregationStatsQueryService aggregationStatsQueryService) : ControllerBase
{
    [HttpGet("pipeline-health")]
    [ProducesResponseType(typeof(PipelineHealthReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PipelineHealthReadModel>> GetPipelineHealthAsync(CancellationToken ct = default)
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
        CancellationToken ct)
    {
        // granularity is required and closed: parse case-insensitively against the
        // allowed values and 400 (ProblemDetails) on anything else, so the unit
        // that the query service inlines into date_trunc can only ever be one we own.
        if (!Enum.TryParse<MatchTimeGranularity>(granularity, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            ModelState.AddModelError(
                nameof(granularity),
                "granularity is required and must be one of: day, week, month, year, patch.");
            return ValidationProblem(ModelState);
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
        CancellationToken ct)
    {
        // Closed set, parsed case-insensitively, same shape as matches-over-time.
        // Narrower on purpose: patch is a property of the games rather than of when we
        // ingested them, and year cannot fill two buckets under the run retention.
        if (!Enum.TryParse<IngestionTimeGranularity>(granularity, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            ModelState.AddModelError(
                nameof(granularity),
                "granularity is required and must be one of: day, week, month.");
            return ValidationProblem(ModelState);
        }

        var readModel = await matchesIngestedQueryService.GetAsync(parsed, windowDays, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Aggregation pipelines snapshot for the admin Aggregation panel: per family
    /// (builds patterns, matchups, timeline leads, powerspikes, mains) the exact
    /// row counts of its tables, champion/patch coverage, data freshness and the
    /// latest recorded run, plus the ingestion backlogs that should read zero when
    /// aggregations are caught up.
    /// </summary>
    [HttpGet("stats/aggregations")]
    [ProducesResponseType(typeof(AggregationsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AggregationsReadModel>> GetAggregationsAsync(CancellationToken ct)
    {
        var readModel = await aggregationStatsQueryService.GetAsync(ct);
        return Ok(readModel);
    }

    [HttpGet("db/tables")]
    [ProducesResponseType(typeof(IReadOnlyList<TableStatRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TableStatRow>>> GetTableStatsAsync(CancellationToken ct)
    {
        var rows = await tableStatsQueryService.GetAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// Storage growth over the last <paramref name="windowDays"/> days plus the
    /// disk-exhaustion forecast (#925), read entirely from the daily snapshots — this
    /// endpoint never scans <c>pg_catalog</c> itself, unlike <c>db/tables</c> above.
    /// Returns an empty model (not 404) before the snapshot step has ever run.
    /// </summary>
    [HttpGet("db/history")]
    [ProducesResponseType(typeof(DbStorageHistoryReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DbStorageHistoryReadModel>> GetDbStorageHistoryAsync(
        [FromQuery] int? windowDays,
        CancellationToken ct)
    {
        var readModel = await dbStorageHistoryQueryService.GetAsync(windowDays, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// One page of recorded process runs, newest first, plus the per-process
    /// rollup (computed over the full filtered set, unaffected by paging).
    /// </summary>
    /// <param name="processName">Restrict to a single process. Omit for all.</param>
    /// <param name="status">A <c>ProcessRunStatus</c> name (case-insensitive). Omit for all.</param>
    /// <param name="since">
    /// Lower bound on <c>StartedAtUtc</c>; also the rollup's in-window cutoff. Omit
    /// for no time floor, in which case the rollup's in-window counts are true
    /// all-time totals (no hidden default window).
    /// </param>
    /// <param name="limit">
    /// Legacy page size, kept for backward compatibility: honoured as
    /// <paramref name="pageSize"/> when that param is absent, superseded by it
    /// otherwise. Prefer <paramref name="pageSize"/>.
    /// </param>
    /// <param name="page">1-based page index (backend clamps to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (backend clamps to [1, 500], default 100).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("process-runs")]
    [ProducesResponseType(typeof(ProcessRunsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProcessRunsReadModel>> GetProcessRunsAsync(
        [FromQuery] string? processName,
        [FromQuery] string? status,
        [FromQuery] DateTime? since,
        [FromQuery] int? limit,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var readModel = await processRunsQueryService.GetAsync(
            processName, status, since, limit, page, pageSize, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Recent pipeline iterations for the admin chain view: one full pass of the
    /// ingestor pipeline per iteration, newest first, each carrying its ordered
    /// process runs (status / duration / summary). Only iteration-stamped runs are
    /// grouped; historical un-grouped rows are surfaced through
    /// <c>GET /ops/process-runs</c> instead.
    /// </summary>
    /// <param name="page">1-based page index (backend clamps to ≥ 1).</param>
    /// <param name="pageSize">Iterations per page (backend clamps to [1, 50], default 10).</param>
    /// <param name="finishedOnly">
    /// When true, excludes the in-flight iteration from both the page and the total
    /// so a completed-history list paginates correctly. Default false.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("process-iterations")]
    [ProducesResponseType(typeof(ProcessIterationsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProcessIterationsReadModel>> GetProcessIterationsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? finishedOnly,
        CancellationToken ct)
    {
        var readModel = await processIterationsQueryService.GetAsync(page, pageSize, finishedOnly ?? false, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// One page of persisted diagnostic logs, newest-first. <paramref name="level"/>
    /// is a minimum-severity threshold; <paramref name="category"/> a
    /// case-insensitive prefix; <paramref name="search"/> a case-insensitive
    /// substring over message/exception; <paramref name="eventType"/> and
    /// <paramref name="process"/> case-insensitive exact matches on the ops-event
    /// name and the producing host ("Api"/"Ingestor"); <paramref name="hasException"/>
    /// true restricts to rows carrying a formatted exception.
    /// </summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(LogsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LogsReadModel>> GetLogsAsync(
        [FromQuery] string? level,
        [FromQuery] string? category,
        [FromQuery] DateTime? since,
        [FromQuery] string? search,
        [FromQuery] string? eventType,
        [FromQuery] string? process,
        [FromQuery] bool? hasException,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var readModel = await logsQueryService.GetAsync(
            level, category, since, search, eventType, process, hasException, page, pageSize, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// One page of recorded process crashes, newest-first. Each row carries the full
    /// report (exception chain, environment + memory/GC snapshot, and the last log
    /// lines before the crash), so the admin Crashes panel needs no separate detail
    /// call. <paramref name="process"/> ("Api"/"Ingestor") and <paramref name="source"/>
    /// (a <c>CrashSource</c> name, case-insensitive) are exact filters;
    /// <paramref name="search"/> matches message/stack-trace case-insensitively;
    /// <paramref name="since"/> is a lower bound on the crash time.
    /// </summary>
    [HttpGet("crashes")]
    [ProducesResponseType(typeof(CrashesReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CrashesReadModel>> GetCrashesAsync(
        [FromQuery] DateTime? since,
        [FromQuery] string? process,
        [FromQuery] string? source,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var readModel = await crashesQueryService.GetAsync(since, process, source, search, page, pageSize, ct);
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
        CancellationToken ct)
    {
        var readModel = await riotApiUsageQueryService.GetAsync(window, endpoint, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// The automated anomaly detectors (#924): one card per detector with its
    /// green/amber/red verdict, headline number, drill-down rows and the configured
    /// thresholds it judged against. A detector that cannot measure reports
    /// <c>unknown</c> — never green — rather than failing the panel.
    /// </summary>
    [HttpGet("data-quality/detectors")]
    [ProducesResponseType(typeof(DataQualityDetectorsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DataQualityDetectorsReadModel>> GetDataQualityDetectorsAsync(
        CancellationToken ct)
    {
        var readModel = await dataQualityDetectorsQueryService.GetDetectorsAsync(ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Per-champion aggregate freshness on the newest patches, stalest first. Split off
    /// the detector payload because it is the one measurement needing a grouped scan of
    /// <c>champion_aggregate_scopes</c> — affordable on an explicit click, not on every
    /// page view.
    /// </summary>
    [HttpGet("data-quality/aggregate-freshness")]
    [ProducesResponseType(typeof(AggregateFreshnessReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AggregateFreshnessReadModel>> GetAggregateFreshnessAsync(
        CancellationToken ct)
    {
        var readModel = await dataQualityDetectorsQueryService.GetAggregateFreshnessAsync(ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Lists matches flagged by the data-quality checks, grouped by issue type and
    /// paged. Each check is queue-scoped so non-applicable rules (e.g. lanes on
    /// ARAM) don't flood the panel. Read-only diagnostics — no repair.
    /// </summary>
    /// <param name="issue">
    /// Restrict to a single check (case-insensitive name: missingTimeline,
    /// wrongParticipantCount, missingTeamPosition, zeroDuration, duplicateChampion).
    /// Omit for all checks.
    /// </param>
    /// <param name="queue">Restrict to one queue id (e.g. 420). Omit for all queues.</param>
    /// <param name="minAgeHours">Only consider matches at least this many hours old.</param>
    /// <param name="page">1-based page index for each issue group's sample.</param>
    /// <param name="pageSize">Per-issue sample size (backend clamps to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("data-quality/incomplete-matches")]
    [ProducesResponseType(typeof(IncompleteMatchesReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncompleteMatchesReadModel>> GetIncompleteMatchesAsync(
        [FromQuery] string? issue,
        [FromQuery] int? queue,
        [FromQuery] int? minAgeHours,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var readModel = await dataQualityQueryService.GetIncompleteMatchesAsync(
            issue, queue, minAgeHours, page, pageSize, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Per-match data-quality detail: both teams laid out by position with the
    /// gaps identified, plus the issue types the match trips. 404 if no such match.
    /// </summary>
    [HttpGet("data-quality/match/{id}")]
    [ProducesResponseType(typeof(MatchDataQualityDetailReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MatchDataQualityDetailReadModel>> GetMatchDataQualityAsync(
        string id,
        CancellationToken ct)
    {
        var readModel = await dataQualityQueryService.GetMatchDetailAsync(id, ct);
        return readModel is null ? NotFound() : Ok(readModel);
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
        return Accepted(new SeedRequestAcceptedResponse
        {
            Id = result.Id,
            Status = result.Status,
            Created = result.Created
        });
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

    /// <summary>
    /// Recent manual seed ("add a main") requests, newest-first. <paramref name="status"/>
    /// is an exact <c>SeedRequestStatus</c> name (case-insensitive; unknown values are
    /// ignored) and <paramref name="search"/> is a case-insensitive substring match on
    /// the Riot ID (gameName/tagLine).
    /// </summary>
    [HttpGet("accounts/seed")]
    [ProducesResponseType(typeof(IReadOnlyList<SeedRequestReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<SeedRequestReadModel>>> GetSeedRequestsAsync(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var readModels = await seedRequestQueryService.GetRecentAsync(status, search, limit, ct);
        return Ok(readModels);
    }

    /// <summary>
    /// Traces one Riot ID through the whole pipeline (#1032): identity and refresh
    /// state, the match-ingestion lease, the candidate funnel, the analysed main
    /// champions and the rank history, in one read-model. Read-only, and
    /// database-only — the API holds no Riot client, so this never resolves a Riot
    /// ID that the pipeline has not already recorded.
    /// <para>
    /// Declared after the literal <c>accounts/seed</c> routes; literal segments
    /// outrank the <c>{nameTag}</c> parameter, so those keep resolving to the seed
    /// endpoints.
    /// </para>
    /// </summary>
    /// <param name="nameTag">
    /// The Riot ID, either as typed (<c>Name#TAG</c>, percent-encoded) or in the
    /// hyphen slug form the public routes use (<c>Name-TAG</c>). 400 when it parses
    /// as neither.
    /// </param>
    /// <param name="region">
    /// Restrict the search to one platform (e.g. "EUW1"). Omit to search every
    /// region — a Riot ID is only unique within a routing region, so the read-model
    /// lists any other account carrying it. 400 on an unknown platform, because
    /// silently answering "never discovered" for a typo would be a lie.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("accounts/{nameTag}")]
    [ProducesResponseType(typeof(AccountExplorerReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountExplorerReadModel>> GetAccountExplorerAsync(
        string nameTag,
        [FromQuery] string? region,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParseRiotId(nameTag, out var parsed))
        {
            return ValidationProblem(
                $"nameTag must be a Riot ID of the form Name#TAG or Name-TAG "
                + $"(at most {NameTagParser.MaxRiotIdLength} characters).");
        }

        string? platformId = null;
        if (!string.IsNullOrWhiteSpace(region))
        {
            if (!PlatformId.TryParse(region.Trim(), out var platform))
            {
                return ValidationProblem(
                    $"region '{region}' is not a known platform route (e.g. EUW1, KR, NA1).");
            }

            platformId = platform.Value;
        }

        // No 404: an unknown Riot ID is a state this endpoint exists to report,
        // and a 404 would render in the admin as a failure rather than an answer.
        var readModel = await accountExplorerQueryService.GetAsync(
            parsed.GameName, parsed.TagLine, platformId, ct);

        return Ok(readModel);
    }

    /// <summary>
    /// Lists main candidates (the ingestion pipeline: New → Scored → Queued →
    /// Processing → Validated, or Rejected), most-relevant first, paged. Filterable
    /// by <paramref name="status"/> and <paramref name="region"/> (PlatformId), and
    /// searchable by <paramref name="search"/> over the joined Riot ID
    /// (gameName/tagLine), PUUID, or — when numeric — champion id. Read-only.
    /// </summary>
    /// <param name="status">
    /// Restrict to a single <c>MainCandidateStatus</c> (case-insensitive name:
    /// new, scored, queued, processing, validated, rejected). Omit for all.
    /// </param>
    /// <param name="region">Restrict to one PlatformId (e.g. "EUW1"). Omit for all.</param>
    /// <param name="search">Riot ID / PUUID / champion-id search. Omit for none.</param>
    /// <param name="page">1-based page index (backend clamps to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (backend clamps to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("candidates")]
    [ProducesResponseType(typeof(CandidatesReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CandidatesReadModel>> GetCandidatesAsync(
        [FromQuery] string? status,
        [FromQuery] string? region,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var readModel = await candidateQueryService.GetCandidatesAsync(
            status, region, search, page, pageSize, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Candidate funnel throughput per period (#1024): intake split by source, scored,
    /// promoted, validated and demoted, from the recorded run summaries.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from <c>main_candidates</c> row counts: retention prunes
    /// stale candidates, so counting rows by status per past period under-reports every
    /// bucket and increasingly so the further back it looks. Bounded by the
    /// <c>process_runs</c> TTL, which the response reports. The validated series is
    /// forward-only and reads null before the counter existed — never zero.
    /// </remarks>
    [HttpGet("candidates/funnel")]
    [ProducesResponseType(typeof(CandidateFunnelReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CandidateFunnelReadModel>> GetCandidateFunnelAsync(
        [FromQuery] string? granularity,
        [FromQuery] int? windowDays,
        CancellationToken ct)
    {
        if (!Enum.TryParse<IngestionTimeGranularity>(granularity, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            ModelState.AddModelError(
                nameof(granularity),
                "granularity is required and must be one of: day, week, month.");
            return ValidationProblem(ModelState);
        }

        var readModel = await candidateFunnelQueryService.GetAsync(parsed, windowDays, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Queue latency for the candidates currently retained (#1024): median and p90 of
    /// discovery → scoring and scoring → validated.
    /// </summary>
    /// <remarks>
    /// A snapshot over the rows that exist right now, not a historical average, and it
    /// takes no window for that reason — pruned candidates are simply not in it. The
    /// companion of <c>candidates/funnel</c>, which is the historical half.
    /// </remarks>
    [HttpGet("candidates/queue-latency")]
    [ProducesResponseType(typeof(CandidateQueueLatencyReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CandidateQueueLatencyReadModel>> GetCandidateQueueLatencyAsync(CancellationToken ct)
    {
        var readModel = await candidateQueueLatencyQueryService.GetAsync(ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Detail for one candidate: its pipeline fields + timestamps, the joined
    /// account identity, the count of ingested matches for its PUUID, and the
    /// linked manual seed request (matched on ResolvedPuuid + platform) when one
    /// exists. 404 if no such candidate.
    /// </summary>
    [HttpGet("candidates/{id:guid}")]
    [ProducesResponseType(typeof(CandidateDetailReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CandidateDetailReadModel>> GetCandidateAsync(Guid id, CancellationToken ct)
    {
        var readModel = await candidateQueryService.GetByIdAsync(id, ct);
        return readModel is null ? NotFound() : Ok(readModel);
    }
}

/// <summary>Request body for <c>POST /ops/accounts/seed</c>.</summary>
public sealed record SeedAccountRequest
{
    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public string? PlatformId { get; init; }
}

/// <summary>
/// 202 body for an accepted seed request: the row id, its current status, and
/// whether it was created by this call. <see cref="Created"/> is <c>true</c> for a
/// freshly-inserted request and <c>false</c> when an existing unprocessed request
/// was returned instead (idempotency) — letting the caller tell a brand-new seed
/// apart from an "already seeded" hit.
/// </summary>
public sealed record SeedRequestAcceptedResponse
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;

    public bool Created { get; init; }
}
