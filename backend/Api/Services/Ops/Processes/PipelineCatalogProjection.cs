using Data.Pipeline;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Processes;

/// <summary>Projects <see cref="PipelineCatalog"/> onto its read model.</summary>
public static class PipelineCatalogProjection
{
    public static PipelineCatalogReadModel ReadModel { get; } = new()
    {
        Steps = PipelineCatalog.Steps
            .Select(step => new PipelineStepReadModel
            {
                Name = step.Name,
                JobMode = step.Mode,
                Lane = LaneId(step.Lane),
                Label = step.Label,
                Description = step.Description
            })
            .ToList(),
        Lanes = PipelineCatalog.Lanes
            .Select(lane => new PipelineLaneReadModel
            {
                Id = lane.Id,
                Label = lane.Label,
                Description = lane.Description,
                JobModes = PipelineCatalog.ModesRunning(lane.Lane).ToList(),
                Steps = PipelineCatalog.InLane(lane.Lane).Select(step => step.Name).ToList()
            })
            .ToList()
    };

    private static string LaneId(PipelineLane lane)
        => PipelineCatalog.Lanes.Single(candidate => candidate.Lane == lane).Id;
}
