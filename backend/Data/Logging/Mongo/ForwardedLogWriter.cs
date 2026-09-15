using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Logging.Mongo;

/// <summary>
/// A log record another process reported to the API for persistence (#1556) — today
/// the public site's and the admin portal's servers, which have no Mongo connection
/// of their own.
/// </summary>
public sealed record ForwardedLogEntry(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    string ProcessName,
    string? Host,
    string? EventType,
    string? RequestMethod,
    string? RequestPath,
    int? StatusCode,
    long? DurationMs);

/// <summary>Queues a <see cref="ForwardedLogEntry"/> on the same channel the host's own loggers use.</summary>
public interface IForwardedLogWriter
{
    /// <summary>False when the log store is off (or muted) in this host; true once queued.</summary>
    bool TryWrite(ForwardedLogEntry entry);
}

/// <summary>
/// Writes forwarded records straight into <see cref="MongoLogChannel"/> rather than
/// through <c>ILogger</c>: a logger stamps its own host's process name, and the whole
/// point of a forwarded row is to say which <em>other</em> process it came from. Going
/// through the channel keeps everything else identical — the same batching, the same
/// truncation, the same drop accounting.
/// </summary>
internal sealed class ForwardedLogWriter(MongoLogChannel channel, IOptions<MongoLoggingOptions> options) : IForwardedLogWriter
{
    private readonly MongoLoggingOptions _options = options.Value;

    public bool TryWrite(ForwardedLogEntry entry)
    {
        if (!_options.IsActive
            || _options.MinimumLevel == LogLevel.None
            || entry.Level < _options.MinimumLevel)
        {
            return false;
        }

        var eventId = entry.EventType is null ? null : OpsEvents.IdOf(entry.EventType);
        return channel.TryWrite(new MongoLogRecord(
            TimestampUtc: entry.TimestampUtc,
            Level: entry.Level,
            Category: entry.Category,
            EventId: eventId ?? 0,
            Message: entry.Message,
            Exception: entry.Exception,
            ProcessName: entry.ProcessName,
            Host: entry.Host ?? "unknown",
            EventType: eventId is null ? null : entry.EventType,
            Request: new MongoLogRequestFields(null, entry.RequestMethod, entry.RequestPath, entry.StatusCode, entry.DurationMs)));
    }
}
