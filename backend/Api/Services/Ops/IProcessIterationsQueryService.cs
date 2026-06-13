using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IProcessIterationsQueryService
{
    /// <summary>
    /// Returns a page of pipeline iterations (newest first), each carrying its
    /// ordered process runs. Only iteration-stamped runs are grouped; un-grouped
    /// historical rows are excluded. When <paramref name="finishedOnly"/> is true,
    /// the in-flight iteration (one with a genuinely Running run) is excluded from
    /// both the page and the total, so a "completed history" list paginates
    /// correctly.
    /// </summary>
    Task<ProcessIterationsReadModel> GetAsync(int? page, int? pageSize, bool finishedOnly, CancellationToken ct);
}
