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
    /// Per-process health over the window starting at <paramref name="since"/>: last status,
    /// last success, in-window failure counts and rate, and the verdict the health grid colours by.
    /// Unbounded (all-time counts) when <paramref name="since"/> is omitted.
    /// </summary>
    /// <param name="processName">Restrict to a single process. Omit for all.</param>
    /// <param name="since">Lower bound of the failure window. Omit for all time.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("process-runs/rollup")]
    [ProducesResponseType(typeof(IReadOnlyList<ProcessRunRollupReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ProcessRunRollupReadModel>>> GetProcessRunRollupAsync(
        [FromQuery] string? processName,
        [FromQuery] DateTime? since,
        CancellationToken ct = default)
        => Ok(await processRunsQueryService.GetRollupAsync(processName, since, ct));

    /// <summary>
    /// The newest pass of each pipeline lane, for the chain view's live tree: one entry per
    /// lane that has run, a <c>Full</c> pass newest on both listed once.
    /// </summary>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("process-iterations/latest")]
    [ProducesResponseType(typeof(IReadOnlyList<ProcessIterationReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ProcessIterationReadModel>>> GetLatestProcessIterationsAsync(
        CancellationToken ct = default)
        => Ok(await processIterationsQueryService.GetLatestPerLaneAsync(ct));

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
    /// <param name="lane">
    /// <c>fetch</c> or <c>aggregate</c>: only the passes that ran that lane (a <c>Full</c> pass
    /// counts for both). Omit, or pass anything else, for every pass.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("process-iterations")]
    [ProducesResponseType(typeof(ProcessIterationsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProcessIterationsReadModel>> GetProcessIterationsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? finishedOnly,
        [FromQuery] string? lane,
        CancellationToken ct = default)
    {
        var readModel = await processIterationsQueryService.GetAsync(page, pageSize, finishedOnly ?? false, lane, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// The pipeline the ingestor runs: its steps in order, with their lane, label and
    /// description, and the lanes that partition them.
    /// </summary>
    [HttpGet("pipeline")]
    [ProducesResponseType(typeof(PipelineCatalogReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public ActionResult<PipelineCatalogReadModel> GetPipeline() => Ok(PipelineCatalogProjection.ReadModel);
}
