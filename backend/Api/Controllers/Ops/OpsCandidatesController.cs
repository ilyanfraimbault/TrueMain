using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Candidates;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The candidate queue that feeds discovery: the rows themselves, the funnel they move through,
/// the stock left at each stage and how long they wait. Four views of one pipeline stage, which
/// is why they share a controller and a filter vocabulary.
/// </summary>
public sealed class OpsCandidatesController(
    ICandidateQueryService candidateQueryService,
    ICandidateFunnelQueryService candidateFunnelQueryService,
    ICandidateStockQueryService candidateStockQueryService,
    ICandidateQueueLatencyQueryService candidateQueueLatencyQueryService) : OpsControllerBase
{
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
        CancellationToken ct = default)
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
        CancellationToken ct = default)
    {
        if (!this.TryParseGranularity<IngestionTimeGranularity>(granularity, out var parsed, out var problem))
        {
            return problem;
        }

        var readModel = await candidateFunnelQueryService.GetAsync(parsed, windowDays, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// The candidate funnel's level per period (#1403): how many candidates sat in each
    /// status at the end of each period, from the hourly snapshots.
    /// </summary>
    /// <remarks>
    /// The companion of <c>candidates/funnel</c>, which measures the same funnel's flow.
    /// Forward-only and never backfilled: periods before the first snapshot are absent
    /// from the series, never zero, because the level then was unmeasured rather than
    /// empty — and unlike a counter, it cannot be reconstructed afterwards
    /// (<c>main_candidates</c> has no <c>QueuedAtUtc</c>, and pruning deletes rows).
    /// </remarks>
    [HttpGet("candidates/stock")]
    [ProducesResponseType(typeof(CandidateStockReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CandidateStockReadModel>> GetCandidateStockAsync(
        [FromQuery] string? granularity,
        [FromQuery] int? windowDays,
        CancellationToken ct = default)
    {
        if (!this.TryParseGranularity<IngestionTimeGranularity>(granularity, out var parsed, out var problem))
        {
            return problem;
        }

        var readModel = await candidateStockQueryService.GetAsync(parsed, windowDays, ct);
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
    public async Task<ActionResult<CandidateQueueLatencyReadModel>> GetCandidateQueueLatencyAsync(CancellationToken ct = default)
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
    public async Task<ActionResult<CandidateDetailReadModel>> GetCandidateAsync(Guid id, CancellationToken ct = default)
    {
        var readModel = await candidateQueryService.GetByIdAsync(id, ct);
        return readModel is null ? NotFound() : Ok(readModel);
    }
}
