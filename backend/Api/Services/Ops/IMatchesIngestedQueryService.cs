using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IMatchesIngestedQueryService
{
    Task<MatchesIngestedReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct);
}
