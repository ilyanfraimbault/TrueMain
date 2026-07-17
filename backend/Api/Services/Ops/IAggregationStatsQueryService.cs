using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IAggregationStatsQueryService
{
    Task<AggregationsReadModel> GetAsync(CancellationToken ct);
}
