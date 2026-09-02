using Ingestor.Options;

namespace Ingestor.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed <see cref="IIterationContext"/>. Registered
/// as a singleton: the single Worker loop sets the iteration at the top of each
/// pass and the (also-singleton) recorder reads it. The AsyncLocal value flows
/// into every awaited process in that pass but is isolated from any other async
/// flow, so the processes stay concurrent-safe.
/// </summary>
public sealed class IterationContext : IIterationContext
{
    private readonly AsyncLocal<Guid?> _current = new();
    private readonly AsyncLocal<JobMode?> _currentMode = new();

    public Guid? CurrentIterationId => _current.Value;

    public JobMode? CurrentJobMode => _currentMode.Value;

    public IIterationScope BeginIteration(JobMode mode)
    {
        var previous = _current.Value;
        var previousMode = _currentMode.Value;
        var iterationId = Guid.NewGuid();
        _current.Value = iterationId;
        _currentMode.Value = mode;
        return new IterationScope(this, iterationId, previous, previousMode);
    }

    private sealed class IterationScope(
        IterationContext owner,
        Guid iterationId,
        Guid? previous,
        JobMode? previousMode)
        : IIterationScope
    {
        private bool _disposed;

        public Guid IterationId => iterationId;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._current.Value = previous;
            owner._currentMode.Value = previousMode;
        }
    }
}
