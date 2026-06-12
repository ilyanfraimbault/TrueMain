using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IProcessIterationsQueryService
{
    /// <summary>
    /// Returns a page of pipeline iterations (newest first), each carrying its
    /// ordered process runs. Only iteration-stamped runs are grouped; un-grouped
    /// historical rows are excluded.
    /// </summary>
    Task<ProcessIterationsReadModel> GetAsync(int? page, int? pageSize, CancellationToken ct);
}
