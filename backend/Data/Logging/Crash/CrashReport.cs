namespace Data.Logging.Crash;

/// <summary>
/// A fully-assembled crash report, built by <see cref="CrashReporter"/> at the
/// moment of a crash and written verbatim to the durable file sink and to the Mongo
/// <c>crashes</c> collection. Kept as a plain serializable model (not the
/// <see cref="CrashReportDocument"/>) so assembling it on a dying thread never
/// touches the Mongo driver.
/// </summary>
/// <remarks>
/// The file line is the source of truth (it survives Mongo being down); the Mongo
/// document is the queryable copy the admin Crashes panel reads. <see cref="Id"/> is
/// generated in-process so a file line and its Mongo document can be correlated even
/// if the Mongo write later times out.
/// </remarks>
public sealed record CrashReport
{
    /// <summary>In-process identity, shared by the file line and the Mongo document.</summary>
    public required Guid Id { get; init; }

    public required DateTime TimestampUtc { get; init; }

    /// <summary>"Api" or "Ingestor" (from <c>MongoLoggingOptions.ProcessName</c>).</summary>
    public required string ProcessName { get; init; }

    public required CrashSource Source { get; init; }

    /// <summary>The top-level exception's type full name; null for an unclean shutdown.</summary>
    public string? ExceptionType { get; init; }

    public string? Message { get; init; }

    public string? StackTrace { get; init; }

    /// <summary>The flattened inner-exception chain (and <c>AggregateException</c> children).</summary>
    public IReadOnlyList<CrashExceptionInfo> InnerExceptions { get; init; } = [];

    public string? Host { get; init; }

    public string? OsDescription { get; init; }

    /// <summary>How long the process had been running, in seconds (best-effort).</summary>
    public double UptimeSeconds { get; init; }

    public string? RuntimeVersion { get; init; }

    public string? AppVersion { get; init; }

    /// <summary>OS working set at the crash (<c>Environment.WorkingSet</c>).</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>Managed heap size at the crash (<c>GC.GetTotalMemory(false)</c>).</summary>
    public long TotalManagedMemoryBytes { get; init; }

    public int Gen0Collections { get; init; }

    public int Gen1Collections { get; init; }

    public int Gen2Collections { get; init; }

    /// <summary>Known only for an unclean-shutdown detection where the OS exit code was recoverable; else null.</summary>
    public int? ExitCode { get; init; }

    /// <summary>The last N log records (Information and above) seen before the crash, oldest first.</summary>
    public IReadOnlyList<CrashLogTailEntry> RecentLogTail { get; init; } = [];
}

/// <summary>One link in a crash's exception chain (the top-level exception's inners).</summary>
public sealed record CrashExceptionInfo(string Type, string Message, string? StackTrace);

/// <summary>
/// One buffered log line captured by <see cref="RecentLogTailProvider"/> and attached
/// to a crash report so the operator can see what led up to the crash.
/// </summary>
public sealed record CrashLogTailEntry(
    DateTime TimestampUtc,
    string Level,
    string Category,
    string Message,
    string? Exception);
