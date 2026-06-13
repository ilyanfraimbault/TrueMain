using System.Text.Json;

namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Process-run feed for the admin Processes panel: one page of individual runs
/// (newest first) plus a per-process rollup over the requested window so the
/// panel can show current health and recent failure volume at a glance.
/// <see cref="Total"/> is the count of all runs matching the active filters
/// (before paging), so the panel can render a pager; the rollup is computed over
/// the full filtered set and is unaffected by paging.
/// </summary>
public sealed record ProcessRunsReadModel
{
    public IReadOnlyList<ProcessRunReadModel> Runs { get; init; } = [];

    public IReadOnlyList<ProcessRunRollupReadModel> Rollup { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>
/// A single recorded process run. <see cref="Status"/> is the
/// <c>ProcessRunStatus</c> name ("Success"/"Failed"/"Running"/"Abandoned"); a
/// stale-heartbeat <c>Running</c> row is reported as "Abandoned" here even though
/// its stored status is still <c>Running</c>. <see cref="Error"/> is the stored
/// failure text (may be null) and <see cref="Summary"/> is the run's JSONB payload
/// surfaced verbatim, or null when none was recorded.
/// </summary>
public sealed record ProcessRunReadModel
{
    public Guid Id { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }

    public DateTime FinishedAtUtc { get; init; }

    public int DurationMs { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? Error { get; init; }

    public string? Host { get; init; }

    /// <summary>
    /// Last liveness heartbeat of an in-flight run, or null for legacy rows / runs
    /// that never beat. The frontend can surface how fresh a <c>Running</c> row is;
    /// the API has already aged a stale one out to "Abandoned" in <see cref="Status"/>.
    /// </summary>
    public DateTime? LastHeartbeatAtUtc { get; init; }

    public JsonElement? Summary { get; init; }
}

/// <summary>
/// Per-process summary over the rollup window. The window follows the caller's
/// <c>since</c>: when <c>since</c> is omitted the window is unbounded, so
/// <see cref="FailureCountInWindow"/> is a true all-time total (≥ any narrower
/// window) rather than a hidden default. <see cref="FailureCountInWindow"/> and
/// <see cref="RunCountInWindow"/> count only runs whose <c>StartedAtUtc</c> falls
/// inside the window; <see cref="FailureRateInWindow"/> is their ratio (0 when no
/// runs fall inside the window). <see cref="LastStatus"/> /
/// <see cref="LastRunAtUtc"/> / <see cref="LastSuccessAtUtc"/> are unbounded, so an
/// idle process still reports its real last state (<see cref="LastSuccessAtUtc"/> is
/// null if the process has never succeeded).
/// </summary>
public sealed record ProcessRunRollupReadModel
{
    public string ProcessName { get; init; } = string.Empty;

    public string LastStatus { get; init; } = string.Empty;

    public DateTime LastRunAtUtc { get; init; }

    public DateTime? LastSuccessAtUtc { get; init; }

    public int FailureCountInWindow { get; init; }

    public int RunCountInWindow { get; init; }

    /// <summary>
    /// Fraction of in-window runs that failed, in <c>[0, 1]</c>. 0 when no runs
    /// fall inside the window. Derived from real run counts (not a fabricated
    /// metric) so the admin can color failure volume by a meaningful rate rather
    /// than an always-positive absolute count.
    /// </summary>
    public double FailureRateInWindow { get; init; }
}
