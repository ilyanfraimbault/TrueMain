using Data.Pipeline;

namespace TrueMain.UnitTests.Fixtures;

internal static class TestPipelineSteps
{
    /// <summary>A pipeline step recorded under <paramref name="name"/>, for stub processes.</summary>
    public static PipelineProcess Named(string name) => PipelineCatalog.Discovery with { Name = name };
}
