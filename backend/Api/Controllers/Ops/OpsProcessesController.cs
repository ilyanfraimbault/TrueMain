using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The ingestor's own record of what it did: completed runs and the iterations inside them.
/// The pair is how a stalled pass is told from a slow one — a run that is still claimed but no
/// longer iterating.
/// </summary>
public sealed class OpsProcessesController(
    IProcessRunsQueryService processRunsQueryService,
    IProcessIterationsQueryService processIterationsQueryService) : OpsControllerBase
{
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
        CancellationToken ct = default)
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
        CancellationToken ct = default)
    {
        var readModel = await processIterationsQueryService.GetAsync(page, pageSize, finishedOnly ?? false, ct);
        return Ok(readModel);
    }
}
