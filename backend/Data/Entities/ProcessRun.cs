using System.Text.Json;

namespace Data.Entities;

public class ProcessRun
{
    public Guid Id { get; set; }

    /// <summary>
    /// Groups every run written during one full pass of the ingestor pipeline
    /// (one <c>RunModeAsync</c> iteration) under a shared id, so the admin can
    /// render each iteration as a chain with its per-process outcomes. Nullable
    /// because rows recorded before per-iteration grouping (and any run written
    /// outside a pipeline pass) carry no iteration.
    /// </summary>
    public Guid? IterationId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    public int DurationMs { get; set; }

    public ProcessRunStatus Status { get; set; }

    public string? Error { get; set; }

    public string? Host { get; set; }

    /// <summary>
    /// Liveness signal for an in-flight run: stamped to <see cref="StartedAtUtc"/>
    /// when the row is written as <see cref="ProcessRunStatus.Running"/> and
    /// refreshed periodically while the process runs. Read queries treat a
    /// <c>Running</c> row whose heartbeat has gone stale (or is null) as
    /// <see cref="ProcessRunStatus.Abandoned"/> rather than genuinely running.
    /// Null for legacy rows written before heartbeats existed.
    /// </summary>
    public DateTime? LastHeartbeatAtUtc { get; set; }

    public JsonDocument? Summary { get; set; }
}

public enum ProcessRunStatus
{
    Success = 0,
    Failed = 1,

    /// <summary>
    /// The process has started and is still in flight. A <c>Running</c> row is
    /// written when the process begins and updated to <see cref="Success"/> or
    /// <see cref="Failed"/> on completion. A row left in this state (e.g. after a
    /// host crash) reads as stale-running rather than a recorded outcome.
    /// </summary>
    Running = 2,

    /// <summary>
    /// A <see cref="Running"/> row whose owner died: detected at ingestor startup
    /// (the single-instance ingestor reconciles every still-<c>Running</c> row to
    /// this state, since nothing it owns can survive a restart), or inferred by
    /// read queries from a stale (or missing) <c>LastHeartbeatAtUtc</c>. Surfaces
    /// the run as never-completed instead of perpetually in-flight.
    /// </summary>
    Abandoned = 3
}
