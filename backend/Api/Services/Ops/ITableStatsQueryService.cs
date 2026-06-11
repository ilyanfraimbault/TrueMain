using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface ITableStatsQueryService
{
    Task<IReadOnlyList<TableStatRow>> GetAsync(CancellationToken ct);
}
