using Ingestor.Options;

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
    /// The <see cref="JobMode"/> the current pass is running, or null outside a pass.
    /// Recorded on every run so a reader can tell which half of the pipeline a pass
    /// covered: since #1362 the sequence is split into a fetch lane and an aggregate
    /// lane, and an iteration that ran one of them is complete, not half-finished.
    /// </summary>
    JobMode? CurrentJobMode { get; }

    /// <summary>
    /// Opens a new iteration on the calling async flow and returns it. The id and the
    /// mode are in effect until the returned scope is disposed, which restores the
    /// prior values (so nested or sequential passes don't leak into one another).
    /// </summary>
    /// <param name="mode">The job mode this pass is running.</param>
    IIterationScope BeginIteration(JobMode mode);
}

/// <summary>An active iteration; disposing it ends the iteration on the flow.</summary>
public interface IIterationScope : IDisposable
{
    Guid IterationId { get; }
}
