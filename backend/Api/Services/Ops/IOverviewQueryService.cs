using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IOverviewQueryService
{
    Task<OverviewReadModel> GetAsync(CancellationToken ct);
}
