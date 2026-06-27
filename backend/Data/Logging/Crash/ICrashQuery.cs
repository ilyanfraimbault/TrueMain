namespace Data.Logging.Crash;

/// <summary>
/// Read query over the <c>crashes</c> collection for the admin Crashes panel. Lives
/// in the Data layer so the Api stays persistence-ignorant and depends only on the
/// <see cref="CrashPage"/> read-model. Mirrors <c>IMongoLogQuery</c>.
/// </summary>
public interface ICrashQuery
{
    Task<CrashPage> GetAsync(
        DateTime? since,
        string? processName,
        string? source,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct);
}

/// <summary>
/// A page of crash rows plus the unpaged total and the effective paging the query
/// actually applied (after clamping).
/// </summary>
public sealed record CrashPage(
    IReadOnlyList<CrashRow> Entries,
    long Total,
    int Page,
    int PageSize);

/// <summary>
/// A single crash as read from Mongo. <see cref="Id"/> is the 24-char hex string of
/// the document's ObjectId; <see cref="Source"/> is the <see cref="CrashSource"/>
/// name. For an unclean shutdown the exception fields are null and the memory fields
/// carry the dead run's last-known snapshot.
/// </summary>
public sealed record CrashRow(
    string Id,
    string ReportId,
    DateTime TimestampUtc,
    string ProcessName,
    string Source,
    string? ExceptionType,
    string? Message,
    string? StackTrace,
    IReadOnlyList<CrashExceptionInfo> InnerExceptions,
    string? Host,
    string? OsDescription,
    double UptimeSeconds,
    string? RuntimeVersion,
    string? AppVersion,
    long WorkingSetBytes,
    long TotalManagedMemoryBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int? ExitCode,
    IReadOnlyList<CrashLogTailEntry> RecentLogTail);
