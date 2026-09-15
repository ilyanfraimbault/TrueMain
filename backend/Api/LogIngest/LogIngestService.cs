using Data.Logging;
using Data.Logging.Mongo;
using TrueMain.Requests.Internal;

namespace TrueMain.LogIngest;

public interface ILogIngestService
{
    LogIngestResult Ingest(LogIngestRequest request);
}

/// <summary>Entries queued, or the per-field errors that rejected the whole batch.</summary>
public sealed record LogIngestResult(int Accepted, IReadOnlyDictionary<string, string> Errors);

/// <summary>
/// Turns a validated <see cref="LogIngestRequest"/> into rows of the ops log store
/// (#1556). The shape is checked by model validation; what needs the catalog or the
/// clock is checked here, and a batch with one bad entry is rejected whole.
/// </summary>
public sealed class LogIngestService(IForwardedLogWriter writer, TimeProvider timeProvider) : ILogIngestService
{
    /// <summary>Older than this, a reported timestamp is a stuck queue or a wrong clock; the row is stamped with the server's time.</summary>
    internal static readonly TimeSpan MaxReportedAge = TimeSpan.FromMinutes(10);

    /// <summary>Clock skew tolerated ahead of the server before the reported timestamp is replaced.</summary>
    internal static readonly TimeSpan MaxReportedSkewAhead = TimeSpan.FromMinutes(1);

    public LogIngestResult Ingest(LogIngestRequest request)
    {
        var entries = request.Entries ?? [];
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var eventType = entries[index].EventType;
            if (eventType is not null && !OpsEvents.ForwardableEventTypes.Contains(eventType, StringComparer.Ordinal))
            {
                errors[$"entries[{index}].eventType"] =
                    $"'{eventType}' is not an event a frontend may report; expected one of "
                    + $"{string.Join(", ", OpsEvents.ForwardableEventTypes)}, or none.";
            }
        }

        if (errors.Count > 0)
        {
            return new LogIngestResult(0, errors);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var accepted = 0;
        foreach (var entry in entries)
        {
            var written = writer.TryWrite(new ForwardedLogEntry(
                TimestampUtc: Stamp(entry.TimestampUtc, now),
                Level: Enum.Parse<LogLevel>(entry.Level!),
                Category: entry.Category!,
                Message: entry.Count is > 1
                    ? $"{entry.Message} ({entry.Count} occurrences since the previous report)"
                    : entry.Message!,
                Exception: entry.Exception,
                ProcessName: request.Process!,
                Host: request.Host,
                EventType: entry.EventType,
                RequestMethod: entry.RequestMethod,
                RequestPath: entry.RequestPath,
                StatusCode: entry.StatusCode,
                DurationMs: entry.DurationMs));
            if (written)
            {
                accepted++;
            }
        }

        return new LogIngestResult(accepted, errors);
    }

    private static DateTime Stamp(DateTime? reported, DateTime now)
    {
        if (reported is not { } value)
        {
            return now;
        }

        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return utc < now - MaxReportedAge || utc > now + MaxReportedSkewAhead ? now : utc;
    }
}
