using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface ILogsQueryService
{
    Task<LogsReadModel> GetAsync(
        string? level,
        string? category,
        DateTime? since,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct);
}
