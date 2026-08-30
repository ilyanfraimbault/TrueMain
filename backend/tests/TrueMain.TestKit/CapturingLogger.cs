using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.TestKit;

/// <summary>
/// One record captured by <see cref="CapturingLogger{T}"/> or
/// <see cref="CapturingLoggerProvider"/>. <see cref="Properties"/> carries the structured
/// state (the <c>{Placeholder}</c> values) so a test can assert on the properties an
/// operator filters on, not just the rendered text.
/// </summary>
public sealed record CapturedLog(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> Properties);

/// <summary>
/// Minimal <see cref="ILogger{TCategoryName}"/> that keeps every record instead of
/// writing it anywhere, so tests can assert on what a component logged (levels,
/// ops event ids, structured properties). Enabled at every level: filtering is
/// the host's job, and a test asserting "nothing was logged" wants to see
/// everything the component tried to emit.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLog> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add(new CapturedLog(
            logLevel,
            eventId,
            formatter(state, exception),
            exception,
            state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// <see cref="ILoggerProvider"/> flavour of <see cref="CapturingLogger{T}"/>, for tests that
/// go through a running host and therefore cannot inject the logger themselves. Captures a
/// single category at or above <c>minimumLevel</c>; every other category gets
/// <see cref="NullLogger"/>, keeping the capture free of EF Core / ASP.NET noise.
/// </summary>
public sealed class CapturingLoggerProvider(string category, LogLevel minimumLevel = LogLevel.Warning) : ILoggerProvider
{
    private readonly List<CapturedLog> _entries = [];

    /// <summary>Snapshot of what has been captured so far; safe to read while the host still logs.</summary>
    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Rendered messages only, for the common "what did the API warn about?" assertion.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries.Select(entry => entry.Message)];
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
        => string.Equals(categoryName, category, StringComparison.Ordinal)
            ? new CategoryLogger(this, minimumLevel)
            : NullLogger.Instance;

    public void Dispose()
    {
    }

    private void Record(CapturedLog entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    private sealed class CategoryLogger(CapturingLoggerProvider owner, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

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

            owner.Record(new CapturedLog(
                logLevel,
                eventId,
                formatter(state, exception),
                exception,
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
        }
    }
}
