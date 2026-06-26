using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Logging.Crash;

/// <summary>
/// A single crash report persisted in the <c>crashes</c> collection, written
/// synchronously and losslessly by <see cref="CrashReporter"/> (never through the
/// lossy batched diagnostic-log channel) and read back by <see cref="CrashQuery"/>
/// for the admin Crashes panel. Follows the conventions of
/// <see cref="Data.Logging.Mongo.MongoLogDocument"/> /
/// <see cref="Data.Logging.Mongo.AuditEventDocument"/>: a server-generated
/// <see cref="Id"/>, a <c>timestampUtc</c> the TTL + descending indexes target, and
/// <c>[BsonIgnoreIfNull]</c> on every optional field so reports written before a
/// field existed keep deserializing.
/// </summary>
public sealed class CrashReportDocument
{
    /// <summary>Server-generated ObjectId; surfaced as a 24-char hex string on the read side.</summary>
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>
    /// The in-process report id (a GUID string), correlating this document with its
    /// file line. Stored as a string rather than a BSON GUID to avoid the driver's
    /// GuidRepresentation requirement.
    /// </summary>
    [BsonElement("reportId")]
    public string ReportId { get; set; } = string.Empty;

    [BsonElement("timestampUtc")]
    public DateTime TimestampUtc { get; set; }

    [BsonElement("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>The <see cref="CrashSource"/> name (e.g. "HostRun", "UncleanShutdown").</summary>
    [BsonElement("source")]
    public string Source { get; set; } = string.Empty;

    [BsonElement("exceptionType")]
    [BsonIgnoreIfNull]
    public string? ExceptionType { get; set; }

    [BsonElement("message")]
    [BsonIgnoreIfNull]
    public string? Message { get; set; }

    [BsonElement("stackTrace")]
    [BsonIgnoreIfNull]
    public string? StackTrace { get; set; }

    [BsonElement("innerExceptions")]
    [BsonIgnoreIfNull]
    public List<CrashExceptionDocument>? InnerExceptions { get; set; }

    [BsonElement("host")]
    [BsonIgnoreIfNull]
    public string? Host { get; set; }

    [BsonElement("osDescription")]
    [BsonIgnoreIfNull]
    public string? OsDescription { get; set; }

    [BsonElement("uptimeSeconds")]
    public double UptimeSeconds { get; set; }

    [BsonElement("runtimeVersion")]
    [BsonIgnoreIfNull]
    public string? RuntimeVersion { get; set; }

    [BsonElement("appVersion")]
    [BsonIgnoreIfNull]
    public string? AppVersion { get; set; }

    [BsonElement("workingSetBytes")]
    public long WorkingSetBytes { get; set; }

    [BsonElement("totalManagedMemoryBytes")]
    public long TotalManagedMemoryBytes { get; set; }

    [BsonElement("gen0Collections")]
    public int Gen0Collections { get; set; }

    [BsonElement("gen1Collections")]
    public int Gen1Collections { get; set; }

    [BsonElement("gen2Collections")]
    public int Gen2Collections { get; set; }

    [BsonElement("exitCode")]
    [BsonIgnoreIfNull]
    public int? ExitCode { get; set; }

    [BsonElement("recentLogTail")]
    [BsonIgnoreIfNull]
    public List<CrashLogTailDocument>? RecentLogTail { get; set; }
}

/// <summary>One link in a persisted crash's exception chain.</summary>
public sealed class CrashExceptionDocument
{
    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("stackTrace")]
    [BsonIgnoreIfNull]
    public string? StackTrace { get; set; }
}

/// <summary>One buffered log line attached to a persisted crash report.</summary>
public sealed class CrashLogTailDocument
{
    [BsonElement("timestampUtc")]
    public DateTime TimestampUtc { get; set; }

    [BsonElement("level")]
    public string Level { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("exception")]
    [BsonIgnoreIfNull]
    public string? Exception { get; set; }
}
