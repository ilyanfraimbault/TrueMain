using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface ICrashesQueryService
{
    Task<CrashesReadModel> GetAsync(
        DateTime? since,
        string? process,
        string? source,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct);
}
