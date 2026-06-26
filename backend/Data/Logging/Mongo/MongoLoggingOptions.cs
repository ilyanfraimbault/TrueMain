using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// Configuration for the MongoDB-backed logging store (<c>MongoLoggerProvider</c>
/// + <c>MongoLogSink</c>) and the operator-action audit writer
/// (<c>MongoAuditLog</c>). Bound from the <c>MongoLogging</c> configuration
/// section so the API and the Ingestor read the same keys.
/// </summary>
/// <remarks>
/// Replaces the Postgres <c>LoggingSink</c> options (#416). The diagnostic-log
/// half keeps the same non-blocking, drop-on-overflow channel knobs the Postgres
/// sink used; the new bits are the Mongo connection, the two collection names and
/// the TTL retention windows that native TTL indexes enforce.
/// </remarks>
public sealed class MongoLoggingOptions
{
    public const string SectionName = "MongoLogging";

    /// <summary>
    /// MongoDB connection string. Overridable per environment via
    /// <c>MongoLogging__ConnectionString</c>. When blank the provider is still
    /// registered but disabled (every record is dropped), so a host with no
    /// Mongo configured boots cleanly rather than crashing.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Database the two collections live in.</summary>
    public string Database { get; set; } = "truemain_logs";

    /// <summary>Collection holding diagnostic <c>ILogger</c> records.</summary>
    public string LogsCollection { get; set; } = "logs";

    /// <summary>Collection holding lossless operator-action audit events.</summary>
    public string AuditCollection { get; set; } = "audit_events";

    /// <summary>
    /// Collection holding per-minute Riot API usage rollups (#93), written by the
    /// Ingestor's HTTP metrics handler (one upsert per minute/endpoint/status) and
    /// read by the admin <c>/ops/riot-usage</c> panel. Lives in the same Mongo
    /// database as the logs so it reuses one connection; the section name stays
    /// <c>MongoLogging</c> for that reason rather than being split out. (Renamed
    /// from the per-call <c>riot_api_calls</c> when the store moved to rollups; the
    /// old collection is left to expire via its TTL.)
    /// </summary>
    public string RiotApiCallsCollection { get; set; } = "riot_api_call_rollups";

    /// <summary>
    /// Master switch. When false (or when <see cref="ConnectionString"/> is
    /// blank) the provider is still registered but drops every record, so
    /// persisted logging can be turned off without code changes.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Diagnostic records below this level are not persisted. Defaults to
    /// <see cref="LogLevel.Warning"/> so the store stays focused on problems
    /// (warnings + errors) rather than mirroring every Information line. One
    /// exception (#444): registered ops events (<see cref="OpsEvents"/>) are
    /// persisted from Information up, so pipeline milestones reach the admin Logs
    /// panel without lowering this floor. <see cref="LogLevel.None"/> disables
    /// persistence entirely, ops events included.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Bound on the in-memory channel between the logger and the draining
    /// background service. When full, the oldest queued record is dropped so a
    /// logging burst can never exhaust memory or block the caller.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>Maximum records flushed to Mongo in a single batch insert.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long the drain loop waits to accumulate a batch before flushing a
    /// partial one, so low-volume errors are still persisted promptly.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Retention window for the diagnostic <c>logs</c> collection, enforced by a
    /// native Mongo TTL index on <c>timestampUtc</c>. Defaults to 90 days — the
    /// operator wants up to 3 months of signal-only history (#444; was 30 days
    /// per #416). Set to <see cref="TimeSpan.Zero"/> or negative to disable the
    /// TTL index (retain indefinitely).
    /// </summary>
    public TimeSpan LogsRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Retention window for the <c>riot_api_call_rollups</c> metrics collection
    /// (#93), enforced by a native Mongo TTL index on <c>bucketStartUtc</c>.
    /// Defaults to 14 days — the panel's widest window is 7 days, so a fortnight
    /// gives headroom while keeping the rollup collection bounded. Set to
    /// <see cref="TimeSpan.Zero"/> or negative to disable the TTL index.
    /// </summary>
    public TimeSpan RiotApiCallsRetention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Collection holding lossless crash reports (one per process crash), written
    /// synchronously by <c>CrashReporter</c> (never the batched diagnostic channel)
    /// and read by the admin Crashes panel.
    /// </summary>
    public string CrashesCollection { get; set; } = "crashes";

    /// <summary>
    /// Retention window for the <c>crashes</c> collection, enforced by a native Mongo
    /// TTL index on <c>timestampUtc</c>. Defaults to 365 days — crashes are rare and
    /// high-value, so they are kept far longer than the diagnostic <c>logs</c>. Set to
    /// <see cref="TimeSpan.Zero"/> or negative to disable the TTL index.
    /// </summary>
    public TimeSpan CrashesRetention { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Directory the durable crash files (<c>{Process}.crash.jsonl</c>) and per-process
    /// sentinels are written to. Must be writable by the container's non-root
    /// <c>app</c> user and mounted on a Docker volume so it survives container
    /// recreation (see compose). Blank disables the file sink (the Mongo copy still works).
    /// </summary>
    public string CrashFilePath { get; set; } = "/home/app/crashes";

    /// <summary>
    /// Size cap for a per-process crash file before it is rolled to
    /// <c>{Process}.crash.1.jsonl</c> (one generation kept). Defaults to ~5 MB.
    /// </summary>
    public long CrashFileMaxBytes { get; set; } = 5_000_000;

    /// <summary>
    /// How many recent log records (Information and above) the in-memory ring buffer
    /// retains to attach to a crash report as the "what led up to it" trail.
    /// </summary>
    public int CrashLogTailSize { get; set; } = 200;

    /// <summary>
    /// Upper bound on the synchronous Mongo write of a crash report. Keeps a Mongo
    /// outage from hanging a dying process — the durable file copy is written first,
    /// so a timed-out Mongo write loses nothing.
    /// </summary>
    public TimeSpan CrashMongoWriteTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Stamped onto every persisted record's <c>processName</c> so the
    /// <c>/ops/logs</c> view can tell which host produced a line (e.g. "Api" vs
    /// "Ingestor"). Null leaves the field empty.
    /// </summary>
    public string? ProcessName { get; set; }

    /// <summary>
    /// True only when logging is enabled <em>and</em> a connection string is
    /// present — the single gate the provider, sink and audit writer share.
    /// </summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(ConnectionString);
}
