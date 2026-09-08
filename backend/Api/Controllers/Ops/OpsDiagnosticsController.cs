using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Diagnostics;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// What went wrong: the application log and the crash reports. Both read Mongo rather than the
/// relational side, both are paged and filtered the same way, and both are what an incident
/// starts from.
/// </summary>
public sealed class OpsDiagnosticsController(
    ILogsQueryService logsQueryService,
    ICrashesQueryService crashesQueryService) : OpsControllerBase
{
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
        CancellationToken ct = default)
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
        CancellationToken ct = default)
    {
        var readModel = await crashesQueryService.GetAsync(since, process, source, search, page, pageSize, ct);
        return Ok(readModel);
    }
}
