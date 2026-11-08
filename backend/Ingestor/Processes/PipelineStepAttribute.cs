using Data.Pipeline;

namespace Ingestor.Processes;

/// <summary>
/// Marks the class that implements one <see cref="PipelineCatalog"/> step; registration finds
/// it by this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineStepAttribute(JobMode mode) : Attribute
{
    public JobMode Mode { get; } = mode;
}
