namespace TrueMain.ReadModels.Ops;

/// <summary>
/// One page of pipeline iterations for the admin chain view. Each iteration is
/// one full pass of the ingestor pipeline (all its processes, in order) grouped
/// under a shared iteration id, newest iteration first. Only runs stamped with an
/// iteration id are returned — historical, un-grouped rows have none and are
/// surfaced through the flat runs feed instead. <see cref="Total"/> is the total
/// number of iterations (across all pages) so the panel can render a pager.
/// </summary>
public sealed record ProcessIterationsReadModel
{
    public IReadOnlyList<ProcessIterationReadModel> Iterations { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>
/// A single pipeline iteration: its id, when it started/last advanced, and the
/// ordered process runs recorded in it (one per process that ran in the pass).
/// </summary>
public sealed record ProcessIterationReadModel
{
    public Guid IterationId { get; init; }

    /// <summary>Earliest <c>StartedAtUtc</c> across the iteration's runs — when the pass began.</summary>
    public DateTime StartedAtUtc { get; init; }

    /// <summary>
    /// Latest run activity in the iteration: the max <c>FinishedAtUtc</c> across its
    /// runs (a still-running run mirrors its start, so this advances as the chain does).
    /// </summary>
    public DateTime LastActivityAtUtc { get; init; }

    /// <summary>
    /// True while any run in the iteration is still <c>Running</c> — i.e. this is the
    /// pass the pipeline is currently in.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>The iteration's runs in pipeline order (by <c>StartedAtUtc</c>).</summary>
    public IReadOnlyList<ProcessRunReadModel> Runs { get; init; } = [];
}
