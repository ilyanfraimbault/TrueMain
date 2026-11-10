namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The ingestion pipeline as the admin draws it: every step in execution order, and the two
/// lanes that partition it. Served from <c>Data.Pipeline.PipelineCatalog</c>, the list the
/// ingestor itself runs.
/// </summary>
public sealed record PipelineCatalogReadModel
{
    /// <summary>Every step, in the order a full pass runs them.</summary>
    public IReadOnlyList<PipelineStepReadModel> Steps { get; init; } = [];

    public IReadOnlyList<PipelineLaneReadModel> Lanes { get; init; } = [];
}

public sealed record PipelineStepReadModel
{
    /// <summary>The <c>processName</c> its runs are recorded under.</summary>
    public required string Name { get; init; }

    /// <summary>The <c>Job:Mode</c> that runs this step alone.</summary>
    public required string JobMode { get; init; }

    /// <summary>The <see cref="PipelineLaneReadModel.Id"/> of its lane.</summary>
    public required string Lane { get; init; }

    public required string Label { get; init; }

    public required string Description { get; init; }
}

public sealed record PipelineLaneReadModel
{
    /// <summary><c>fetch</c> or <c>aggregate</c>, as the <c>lane</c> filter of the iterations feed accepts.</summary>
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Description { get; init; }

    /// <summary>The <c>Job:Mode</c> values whose passes run this lane.</summary>
    public IReadOnlyList<string> JobModes { get; init; } = [];

    /// <summary>Its step names, in pipeline order.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];
}
