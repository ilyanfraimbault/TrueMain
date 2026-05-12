using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IPipelineHealthQueryService
{
    Task<PipelineHealthReadModel> GetAsync(CancellationToken ct);
}
