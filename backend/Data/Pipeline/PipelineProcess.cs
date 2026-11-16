namespace Data.Pipeline;

/// <summary>Which ingestor container runs a step (#1362).</summary>
public enum PipelineLane
{
    /// <summary>Riot-bound work, paced by the per-region limiter.</summary>
    Fetch,

    /// <summary>Postgres-bound work: folds, retention, snapshots.</summary>
    Aggregate
}

/// <summary>One lane as the admin draws it.</summary>
public sealed record PipelineLaneDescriptor(PipelineLane Lane, string Id, string Label, string Description);

/// <summary>
/// One step of the ingestion pipeline: everything the ingestor, the Api and the admin need to
/// know about it besides its code.
/// </summary>
public sealed record PipelineProcess
{
    /// <summary>The <c>Job:Mode</c> that runs this step alone, and its DI key.</summary>
    public required JobMode Mode { get; init; }

    /// <summary>
    /// The key every run is persisted under (<c>process_runs.ProcessName</c>). Changing it
    /// splits the history in two, so it outlives renames of the code.
    /// </summary>
    public required string Name { get; init; }

    public required PipelineLane Lane { get; init; }

    public required string Label { get; init; }

    /// <summary>What the step does, in one or two sentences, for the admin's chain view.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Steps whose output this one reads, so they come earlier in <see cref="PipelineCatalog.Steps"/>.
    /// A step in the other lane only orders <see cref="JobMode.Full"/>: the two lanes run on
    /// their own cadences.
    /// </summary>
    public IReadOnlyList<JobMode> RunsAfter { get; init; } = [];

    /// <summary>Folds each match once into a champion aggregate the site reads.</summary>
    public bool IsMatchFold { get; init; }

    /// <summary>
    /// Share of a regional route's Riot window held for this step while it is active (#1457),
    /// so the permanent fetch workers cannot starve it. Overridable per host through
    /// <c>RiotRateLimit:ReservedShares</c>.
    /// </summary>
    public double? ReservedRiotShare { get; init; }
}
