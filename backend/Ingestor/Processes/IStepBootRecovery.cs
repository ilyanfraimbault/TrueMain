using Data.Pipeline;

namespace Ingestor.Processes;

/// <summary>
/// Work a step's previous incarnation left half-done — claims it held, requests it was
/// resolving — settled once at boot, before the first pass, by the lane that owns the step.
/// </summary>
/// <remarks>
/// A lane runs as a single instance, so at boot nothing it owns can be live: whatever is still
/// held was stranded by the container that died. Only the owning lane recovers it, because the
/// other lane's work sits in the same tables and is live right now (#1362, #1513).
/// The <see cref="Worker"/> runs every registered recovery of a step it owns and never lets one
/// stop the boot; each step's own fallback (a lease, a reap) still covers what a failed
/// recovery leaves.
/// </remarks>
public interface IStepBootRecovery
{
    /// <summary>The step whose leftovers this settles.</summary>
    PipelineProcess Step { get; }

    /// <summary>Settles the step's leftovers.</summary>
    Task RecoverAsync(CancellationToken ct);
}
