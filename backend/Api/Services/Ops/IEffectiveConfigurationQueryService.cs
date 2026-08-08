using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IEffectiveConfigurationQueryService
{
    Task<EffectiveConfigurationOverviewReadModel> GetAsync(CancellationToken ct);
}
