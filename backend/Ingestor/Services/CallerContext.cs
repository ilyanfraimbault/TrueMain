namespace Ingestor.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed <see cref="ICallerContext"/>. Registered as
/// a singleton, mirroring <see cref="IterationContext"/>: the Worker sets the
/// caller at the top of each process's run and the (also-singleton) metrics
/// handler reads it. The AsyncLocal value flows into everything the process
/// awaits but is isolated from any other async flow.
/// </summary>
public sealed class CallerContext : ICallerContext
{
    private readonly AsyncLocal<string?> _current = new();

    public string? CurrentCaller => _current.Value;

    public IDisposable BeginCall(string processName)
    {
        var previous = _current.Value;
        _current.Value = processName;
        return new CallScope(this, previous);
    }

    private sealed class CallScope(CallerContext owner, string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._current.Value = previous;
        }
    }
}
