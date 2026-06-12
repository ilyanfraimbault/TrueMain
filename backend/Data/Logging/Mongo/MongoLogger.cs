using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// <see cref="ILogger"/> that snapshots qualifying events and hands them to the
/// shared <see cref="MongoLogChannel"/> for the background sink to persist. One
/// instance per category, created by <see cref="MongoLoggerProvider"/>.
/// </summary>
internal sealed class MongoLogger(
    string category,
    MongoLogChannel channel,
    MongoLoggingOptions options,
    string host) : ILogger
{
    // Categories whose own logging the sink must never re-capture, or it would
    // feed the Mongo driver's own diagnostics back into the channel it is
    // draining and loop. The driver namespace covers the persistence client; the
    // sink's namespace covers its own diagnostics.
    private static readonly string[] ExcludedCategoryPrefixes =
    [
        "MongoDB",
        "Data.Logging"
    ];

    private readonly bool _categoryExcluded =
        Array.Exists(ExcludedCategoryPrefixes, prefix =>
            category.StartsWith(prefix, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => options.IsActive
           && !_categoryExcluded
           && logLevel != LogLevel.None
           // Either at/above the configured floor, or a *candidate* for the
           // ops-event bypass: registered domain events (see OpsEvents) log at
           // Information, below the usual Warning floor, and must still be
           // persisted. IsEnabled cannot see the EventId, so it only admits the
           // level; Log() makes the final call. MinimumLevel=None keeps meaning
           // "persist nothing" (test hosts rely on it to mute the sink).
           && (logLevel >= options.MinimumLevel
               || (options.MinimumLevel != LogLevel.None && logLevel >= OpsEvents.PersistedFloor));

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Ops-event bypass, second half: a record below the configured floor is
        // persisted only when its EventId is a registered domain event. The name
        // is resolved for every persisted record (not just sub-floor ones) so an
        // event logged at Warning+ is still filterable by eventType on /ops/logs.
        var eventType = OpsEvents.Resolve(eventId);
        if (logLevel < options.MinimumLevel && eventType is null)
        {
            return;
        }

        // A sink failure must never escape into the logging pipeline (it would
        // surface as an exception at an arbitrary LogXxx call site, or recurse).
        // Swallow everything and fall back to Console.Error.
        try
        {
            var message = formatter(state, exception);
            var record = new MongoLogRecord(
                TimestampUtc: DateTime.UtcNow,
                Level: logLevel,
                Category: category,
                EventId: eventId.Id,
                Message: message ?? string.Empty,
                Exception: exception?.ToString(),
                ProcessName: options.ProcessName,
                Host: host,
                EventType: eventType);

            channel.TryWrite(record);
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.WriteLine($"[MongoLogger] dropped a log record: {ex}");
            }
            catch
            {
                // Even Console.Error can throw (closed handle); there is nothing
                // safe left to do, so give up silently rather than recurse.
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
