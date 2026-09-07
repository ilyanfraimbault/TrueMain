using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Database;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// What the database weighs: per-table sizes now, and the stored history behind them with its
/// forecast. Disk is the resource this project has actually run out of, so these two are read
/// together and kept apart from the pipeline's own counters.
/// </summary>
public sealed class OpsDatabaseController(
    ITableStatsQueryService tableStatsQueryService,
    IDbStorageHistoryQueryService dbStorageHistoryQueryService) : OpsControllerBase
{
    [HttpGet("db/tables")]
    [ProducesResponseType(typeof(IReadOnlyList<TableStatRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TableStatRow>>> GetTableStatsAsync(CancellationToken ct = default)
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
        CancellationToken ct = default)
    {
        var readModel = await dbStorageHistoryQueryService.GetAsync(windowDays, ct);
        return Ok(readModel);
    }
}
