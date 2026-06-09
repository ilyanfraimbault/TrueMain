using Microsoft.Extensions.Logging;

namespace Data.Logging;

/// <summary>
/// Immutable snapshot of a single log event, captured by
/// <see cref="DatabaseLogger"/> and queued for the background sink. Kept as a
/// plain record (not the EF <c>LogEntry</c> entity) so producing it never
/// touches EF on the caller's thread.
/// </summary>
internal sealed record LogRecord(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    int EventId,
    string Message,
    string? Exception,
    string? ProcessName,
    string Host);
