using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the "seed by Riot ID" intake: a single request by id, and the
/// recent-requests list (optionally filtered by status) backing the admin
/// panel's history. Both project read-models with <c>AsNoTracking</c>.
/// </summary>
public interface ISeedRequestQueryService
{
    Task<SeedRequestReadModel?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<SeedRequestReadModel>> GetRecentAsync(string? status, int? limit, CancellationToken ct);
}
