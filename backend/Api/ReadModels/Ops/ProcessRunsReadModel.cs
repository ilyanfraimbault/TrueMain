using System.Text.Json;

namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Process-run feed for the admin Processes panel: the most recent individual runs
/// (newest first, capped by the requested limit) plus a per-process rollup over the
/// requested window so the panel can show current health and recent failure volume
/// at a glance.
/// </summary>
public sealed record ProcessRunsReadModel
{
    public IReadOnlyList<ProcessRunReadModel> Runs { get; init; } = [];

    public IReadOnlyList<ProcessRunRollupReadModel> Rollup { get; init; } = [];
}

/// <summary>
/// A single recorded process run. <see cref="Status"/> is the
/// <c>ProcessRunStatus</c> name ("Success"/"Failed"); <see cref="Error"/> is the
/// stored failure text (may be null) and <see cref="Summary"/> is the run's JSONB
/// payload surfaced verbatim, or null when none was recorded.
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

    public JsonElement? Summary { get; init; }
}

/// <summary>
/// Per-process summary over the rollup window. <see cref="FailureCountInWindow"/>
/// counts only runs whose <c>StartedAtUtc</c> falls inside the window, while
/// <see cref="LastSuccessAtUtc"/> is the most recent successful finish regardless of
/// the window (null if the process has never succeeded).
/// </summary>
public sealed record ProcessRunRollupReadModel
{
    public string ProcessName { get; init; } = string.Empty;

    public string LastStatus { get; init; } = string.Empty;

    public DateTime LastRunAtUtc { get; init; }

    public DateTime? LastSuccessAtUtc { get; init; }

    public int FailureCountInWindow { get; init; }
}
