using System.Collections.Concurrent;
using Data.Logging.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Logging.Crash;

/// <summary>
/// An <see cref="ILoggerProvider"/> that keeps the last N log records (Information
/// and above) in a fixed-size ring buffer so <see cref="CrashReporter"/> can attach
/// them to a crash report — the "what led up to the crash" breadcrumb trail.
/// </summary>
/// <remarks>
/// Implemented as a standalone provider rather than a tap on
/// <see cref="MongoLogChannel"/>: that channel only ever receives Warning+ records
/// (filtered upstream by <c>MongoLogger.IsEnabled</c>), so tapping it would drop the
/// Information-level breadcrumbs that usually explain a crash. The same one instance
/// is registered both as this concrete type (so the reporter can call
/// <see cref="Snapshot"/>) and as a logging provider (so it receives records). The
/// <c>[ProviderAlias("CrashTail")]</c> lets <c>Logging:CrashTail:LogLevel</c> tune it
/// independently if ever needed.
/// </remarks>
[ProviderAlias("CrashTail")]
internal sealed class RecentLogTailProvider : ILoggerProvider
{
    // Categories whose own logging must never be captured, mirroring MongoLogger:
    // the Mongo driver and this assembly's own diagnostics would otherwise add noise
    // (and, for the driver, churn during the crash-time Mongo write).
    private static readonly string[] ExcludedCategoryPrefixes =
    [
        "MongoDB",
        "Data.Logging"
    ];

    private readonly int _capacity;
    private readonly CrashLogTailEntry?[] _buffer;
    private readonly object _gate = new();
    private long _count;
    private readonly ConcurrentDictionary<string, TailLogger> _loggers = new(StringComparer.Ordinal);

    public RecentLogTailProvider(IOptions<MongoLoggingOptions> options)
    {
        _capacity = Math.Max(1, options.Value.CrashLogTailSize);
        _buffer = new CrashLogTailEntry?[_capacity];
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new TailLogger(name, this));

    public void Dispose() => _loggers.Clear();

    private void Append(CrashLogTailEntry entry)
    {
        lock (_gate)
        {
            _buffer[(int)(_count % _capacity)] = entry;
            _count++;
        }
    }

    /// <summary>The buffered records oldest-first. Safe to call from a crashing thread.</summary>
    public IReadOnlyList<CrashLogTailEntry> Snapshot()
    {
        lock (_gate)
        {
            var size = (int)Math.Min(_count, _capacity);
            // When the ring has wrapped, the oldest live entry sits just past the
            // newest write position; otherwise entries fill 0..count-1 in order.
            var start = _count <= _capacity ? 0 : (int)(_count % _capacity);
            var result = new List<CrashLogTailEntry>(size);
            for (var i = 0; i < size; i++)
            {
                var entry = _buffer[(start + i) % _capacity];
                if (entry is not null)
                {
                    result.Add(entry);
                }
            }

            return result;
        }
    }

    private sealed class TailLogger(string category, RecentLogTailProvider owner) : ILogger
    {
        private readonly bool _excluded =
            Array.Exists(ExcludedCategoryPrefixes, prefix =>
                category.StartsWith(prefix, StringComparison.Ordinal));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => !_excluded && logLevel != LogLevel.None && logLevel >= LogLevel.Information;

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

            try
            {
                var message = formatter(state, exception);
                owner.Append(new CrashLogTailEntry(
                    DateTime.UtcNow,
                    logLevel.ToString(),
                    category,
                    message ?? string.Empty,
                    exception?.ToString()));
            }
            catch
            {
                // The tail is best-effort context; a formatting failure must never
                // disturb the logging pipeline it sits in.
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
}
