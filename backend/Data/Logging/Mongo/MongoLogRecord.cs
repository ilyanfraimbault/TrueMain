using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// Immutable snapshot of a single log event, captured by
/// <see cref="MongoLogger"/> and queued for the background
/// <see cref="MongoLogSink"/>. Kept as a plain record (not the
/// <see cref="MongoLogDocument"/>) so producing it never touches the Mongo
/// driver on the caller's thread.
/// </summary>
internal sealed record MongoLogRecord(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    int EventId,
    string Message,
    string? Exception,
    string? ProcessName,
    string Host);
