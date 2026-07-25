using Microsoft.Extensions.Logging;

namespace TrueMain.UnitTests.Fixtures;

/// <summary>
/// One record captured by <see cref="CapturingLogger{T}"/>. <see cref="Properties"/>
/// carries the structured state (the <c>{Placeholder}</c> values) so a test can
/// assert on the properties an operator filters on, not just the rendered text.
/// </summary>
internal sealed record CapturedLog(
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
internal sealed class CapturingLogger<T> : ILogger<T>
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
