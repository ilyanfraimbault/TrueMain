namespace Ingestor.Services;

/// <summary>
/// Carries the id of the pipeline iteration (one full <c>RunModeAsync</c> pass)
/// currently executing on the calling async flow. The Worker begins a fresh
/// iteration at the start of each pass; <see cref="ProcessRunRecorder"/> reads
/// <see cref="CurrentIterationId"/> when it writes a run so every run of the pass
/// is stamped with the same iteration. Backed by an <c>AsyncLocal</c>, so the id
/// flows down the await chain without being shared across concurrent passes — a
/// run recorded outside any pass simply reads <see langword="null"/>.
/// </summary>
public interface IIterationContext
{
    /// <summary>The iteration the calling flow is in, or null when outside a pass.</summary>
    Guid? CurrentIterationId { get; }

    /// <summary>
    /// Opens a new iteration on the calling async flow and returns it. The id is
    /// in effect until the returned scope is disposed, which restores the prior
    /// value (so nested or sequential passes don't leak into one another).
    /// </summary>
    IIterationScope BeginIteration();
}

/// <summary>An active iteration; disposing it ends the iteration on the flow.</summary>
public interface IIterationScope : IDisposable
{
    Guid IterationId { get; }
}
