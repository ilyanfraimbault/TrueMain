using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface ICandidateFunnelQueryService
{
    Task<CandidateFunnelReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct);
}
