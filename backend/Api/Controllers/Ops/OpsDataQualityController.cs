using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.DataQuality;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// Whether the data we hold is trustworthy: the detector suite, aggregate freshness, matches
/// that never completed, and the per-match drill-down those three lead to. Read as one surface
/// because a detector firing is only actionable next to the row it fired on.
/// </summary>
public sealed class OpsDataQualityController(
    IDataQualityQueryService dataQualityQueryService,
    IDataQualityDetectorsQueryService dataQualityDetectorsQueryService) : OpsControllerBase
{
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
        CancellationToken ct = default)
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
        CancellationToken ct = default)
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
        CancellationToken ct = default)
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
        CancellationToken ct = default)
    {
        var readModel = await dataQualityQueryService.GetMatchDetailAsync(id, ct);
        return readModel is null ? NotFound() : Ok(readModel);
    }
}
