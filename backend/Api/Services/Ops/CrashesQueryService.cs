using Data.Logging.Crash;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads persisted crash reports for the admin Crashes panel. Thin adapter over the
/// Data-layer <see cref="ICrashQuery"/> (the Mongo filter semantics and paging live in
/// Data), mapping the <see cref="CrashPage"/> read-model onto the
/// <see cref="CrashesReadModel"/> API contract and riding the static source/process
/// catalogs on every response — mirrors <c>LogsQueryService</c>.
/// </summary>
public sealed class CrashesQueryService(ICrashQuery query) : ICrashesQueryService
{
    private static readonly IReadOnlyList<string> ProcessNames = ["Api", "Ingestor"];
    private static readonly IReadOnlyList<string> SourceNames = Enum.GetNames<CrashSource>();

    public async Task<CrashesReadModel> GetAsync(
        DateTime? since,
        string? process,
        string? source,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var result = await query.GetAsync(since, process, source, search, page, pageSize, ct);

        return new CrashesReadModel
        {
            Entries = result.Entries.Select(MapRow).ToList(),
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize,
            Sources = SourceNames,
            Processes = ProcessNames
        };
    }

    private static CrashReportReadModel MapRow(CrashRow row) => new()
    {
        Id = row.Id,
        TimestampUtc = row.TimestampUtc,
        ProcessName = row.ProcessName,
        Source = row.Source,
        ExceptionType = row.ExceptionType,
        Message = row.Message,
        StackTrace = row.StackTrace,
        InnerExceptions = row.InnerExceptions
            .Select(e => new CrashExceptionReadModel { Type = e.Type, Message = e.Message, StackTrace = e.StackTrace })
            .ToList(),
        Host = row.Host,
        OsDescription = row.OsDescription,
        UptimeSeconds = row.UptimeSeconds,
        RuntimeVersion = row.RuntimeVersion,
        AppVersion = row.AppVersion,
        WorkingSetBytes = row.WorkingSetBytes,
        TotalManagedMemoryBytes = row.TotalManagedMemoryBytes,
        Gen0Collections = row.Gen0Collections,
        Gen1Collections = row.Gen1Collections,
        Gen2Collections = row.Gen2Collections,
        ExitCode = row.ExitCode,
        RecentLogTail = row.RecentLogTail
            .Select(e => new CrashLogTailReadModel
            {
                TimestampUtc = e.TimestampUtc,
                Level = e.Level,
                Category = e.Category,
                Message = e.Message,
                Exception = e.Exception
            })
            .ToList()
    };
}
