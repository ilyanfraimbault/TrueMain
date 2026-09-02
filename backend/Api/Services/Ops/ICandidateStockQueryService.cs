using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface ICandidateStockQueryService
{
    Task<CandidateStockReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct);
}
