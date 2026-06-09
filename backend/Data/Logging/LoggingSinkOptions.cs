using Microsoft.Extensions.Logging;

namespace Data.Logging;

/// <summary>
/// Configuration for the database logging sink (<c>DatabaseLoggerProvider</c> +
/// <c>DatabaseLogSink</c>). Bound from the <c>LoggingSink</c> configuration
/// section so the API and the Ingestor read the same keys.
/// </summary>
public sealed class LoggingSinkOptions
{
    public const string SectionName = "LoggingSink";

    /// <summary>
    /// Master switch. When false the provider is still registered but drops
    /// every record, so persisted logging can be turned off without code
    /// changes.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Records below this level are not persisted. Defaults to
    /// <see cref="LogLevel.Warning"/> so the store stays focused on problems
    /// (warnings + errors) rather than mirroring every Information line.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Bound on the in-memory channel between the logger and the draining
    /// background service. When full, the oldest queued record is dropped so a
    /// logging burst can never exhaust memory or block the caller.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>Maximum records flushed to the database in a single batch insert.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long the drain loop waits to accumulate a batch before flushing a
    /// partial one, so low-volume errors are still persisted promptly.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Stamped onto every persisted record's <c>ProcessName</c> so the
    /// <c>/ops/logs</c> view can tell which host produced a line (e.g.
    /// "Api" vs "Ingestor"). Null leaves the column empty. Per-job attribution
    /// (which Ingestor job failed) lives in the message/category, not here.
    /// </summary>
    public string? ProcessName { get; set; }
}
