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
