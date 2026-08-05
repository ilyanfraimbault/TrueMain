using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface ICandidateQueueLatencyQueryService
{
    Task<CandidateQueueLatencyReadModel> GetAsync(CancellationToken ct);
}
