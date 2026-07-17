namespace TrueMain.ReadModels.Ops;

/// <summary>
/// A page of recorded process crashes for the admin Crashes panel. Entries are
/// newest-first. <see cref="Total"/> is the count of all rows matching the active
/// filters (before paging). <see cref="Sources"/> and <see cref="Processes"/> ride on
/// every response (static catalogs) so the panel can populate its filter selects
/// without a Mongo <c>distinct</c>.
/// </summary>
public sealed record CrashesReadModel
{
    public IReadOnlyList<CrashReportReadModel> Entries { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>Every <c>CrashSource</c> name, for the source filter.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>The producing processes ("Api", "Ingestor"), for the process filter.</summary>
    public IReadOnlyList<string> Processes { get; init; } = [];
}

/// <summary>
/// A single recorded crash. <see cref="Source"/> is the trigger ("HostRun",
/// "AppDomainUnhandled", "TaskSchedulerUnobserved", "UncleanShutdown"). For an
/// unclean shutdown the exception fields are null and the memory fields carry the
/// dead run's last-known snapshot (the OOM signal).
/// </summary>
public sealed record CrashReportReadModel
{
    /// <summary>The crash document's identifier — a 24-char hex string (the Mongo ObjectId).</summary>
    public string Id { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Plain-language reading of the crash (#722), derived from the source, the
    /// exception chain and — for unclean shutdowns — the memory snapshot and exit
    /// code. Heuristic display text; the raw fields below remain authoritative.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;

    public string? ExceptionType { get; init; }

    public string? Message { get; init; }

    public string? StackTrace { get; init; }

    public IReadOnlyList<CrashExceptionReadModel> InnerExceptions { get; init; } = [];

    public string? Host { get; init; }

    public string? OsDescription { get; init; }

    public double UptimeSeconds { get; init; }

    public string? RuntimeVersion { get; init; }

    public string? AppVersion { get; init; }

    public long WorkingSetBytes { get; init; }

    public long TotalManagedMemoryBytes { get; init; }

    public int Gen0Collections { get; init; }

    public int Gen1Collections { get; init; }

    public int Gen2Collections { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>The last log lines (Information+) before the crash, oldest-first.</summary>
    public IReadOnlyList<CrashLogTailReadModel> RecentLogTail { get; init; } = [];
}

/// <summary>One link in a crash's exception chain.</summary>
public sealed record CrashExceptionReadModel
{
    public string Type { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? StackTrace { get; init; }
}

/// <summary>One buffered log line attached to a crash report.</summary>
public sealed record CrashLogTailReadModel
{
    public DateTime TimestampUtc { get; init; }

    public string Level { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Exception { get; init; }
}
