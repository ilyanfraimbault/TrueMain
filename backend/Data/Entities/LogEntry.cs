namespace Data.Entities;

/// <summary>
/// A single persisted application/process log record. Captured by the
/// <c>DatabaseLoggerProvider</c> sink (see <c>Data/Logging</c>) and surfaced by
/// the admin <c>/ops/logs</c> endpoint. Unlike <see cref="ProcessRun"/> — which
/// records one row per process invocation with success/failure — this is a
/// general, queryable log store fed straight from <c>ILogger</c>, so an Ingestor
/// exception logged via <c>ILogger.LogError</c> lands here automatically.
/// </summary>
// TODO(#412 follow-up): this table is append-only and will grow unbounded.
// Add a retention sweep — a future LogRetentionProcess in the Ingestor,
// registered with AddRecordedProcess like MatchDataRetentionProcess, that
// deletes rows older than a configurable window (by TimestampUtc, which is
// indexed descending for exactly this). Retention is intentionally out of scope
// for the initial logging sink.
public class LogEntry
{
    public Guid Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// The <c>Microsoft.Extensions.Logging.LogLevel</c> name (e.g. "Warning",
    /// "Error"). Stored as text rather than the numeric enum so the column is
    /// human-readable in ad-hoc SQL and the <c>/ops/logs</c> level filter can
    /// match on the name without a lookup.
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>The logger category (typically the source type's full name).</summary>
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Formatted exception (<see cref="Exception.ToString"/>), when one was logged.</summary>
    public string? Exception { get; set; }

    /// <summary>
    /// The originating process name when known. The Ingestor stamps this so a
    /// failure can be attributed to a specific job; the API leaves it null.
    /// </summary>
    public string? ProcessName { get; set; }

    /// <summary>Machine name the record was produced on.</summary>
    public string? Host { get; set; }

    public int? EventId { get; set; }
}
