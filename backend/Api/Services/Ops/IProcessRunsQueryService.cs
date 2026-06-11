using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IProcessRunsQueryService
{
    Task<ProcessRunsReadModel> GetAsync(
        string? processName,
        string? status,
        DateTime? since,
        int? limit,
        CancellationToken ct);
}
