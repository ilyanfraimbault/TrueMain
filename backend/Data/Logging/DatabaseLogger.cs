using Microsoft.Extensions.Logging;

namespace Data.Logging;

/// <summary>
/// <see cref="ILogger"/> that snapshots qualifying events and hands them to the
/// shared <see cref="DatabaseLogChannel"/> for the background sink to persist.
/// One instance per category, created by <see cref="DatabaseLoggerProvider"/>.
/// </summary>
internal sealed class DatabaseLogger(
    string category,
    DatabaseLogChannel channel,
    LoggingSinkOptions options,
    string host) : ILogger
{
    // Categories whose own logging the sink must never re-capture, or it would
    // feed its own EF/Npgsql writes back into the channel and loop. EF and
    // Npgsql cover the persistence stack; the sink's namespace covers its own
    // diagnostics.
    private static readonly string[] ExcludedCategoryPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Data.Logging"
    ];

    private readonly bool _categoryExcluded =
        Array.Exists(ExcludedCategoryPrefixes, prefix =>
            category.StartsWith(prefix, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => options.Enabled
           && !_categoryExcluded
           && logLevel != LogLevel.None
           && logLevel >= options.MinimumLevel;

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

        // A sink failure must never escape into the logging pipeline (it would
        // surface as an exception at an arbitrary LogXxx call site, or recurse).
        // Swallow everything and fall back to Console.Error.
        try
        {
            var message = formatter(state, exception);
            var record = new LogRecord(
                TimestampUtc: DateTime.UtcNow,
                Level: logLevel,
                Category: category,
                EventId: eventId.Id,
                Message: message ?? string.Empty,
                Exception: exception?.ToString(),
                ProcessName: options.ProcessName,
                Host: host);

            channel.TryWrite(record);
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.WriteLine($"[DatabaseLogger] dropped a log record: {ex}");
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
