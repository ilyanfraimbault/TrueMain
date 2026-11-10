using AwesomeAssertions;
using Data.Pipeline;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.UnitTests;

/// <summary>
/// <c>GET /ops/pipeline</c> as the admin reads it: each step with the mode that runs it alone,
/// each lane with the composite modes that run it.
/// </summary>
public sealed class PipelineCatalogProjectionTests
{
    [Fact]
    public void ReadModel_NamesTheModeThatRunsEachStepAlone()
        => PipelineCatalogProjection.ReadModel.Steps
            .Single(step => step.Name == PipelineCatalog.LadderSync.Name)
            .JobMode.Should().Be(JobMode.LadderSyncOnly);

    [Fact]
    public void ReadModel_ListsTheCompositeModesRunningEachLane()
    {
        var lanes = PipelineCatalogProjection.ReadModel.Lanes.ToDictionary(lane => lane.Id);

        lanes["fetch"].JobModes.Should().Contain([JobMode.Full, JobMode.FetchLane]);
        lanes["aggregate"].JobModes.Should().Contain([JobMode.Full, JobMode.AggregateLane]);
        lanes["aggregate"].JobModes.Should().NotContain(JobMode.FetchLane);
    }
}
