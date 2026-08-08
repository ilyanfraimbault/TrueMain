using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Answers "is the current patch servable?" for the admin patch-coverage view (#1033).
/// </summary>
public interface IPatchCoverageQueryService
{
    /// <summary>
    /// Ingestion, aggregate coverage against the public games floor, and per-fold state
    /// for the newest <c>PatchCoverage:PatchCount</c> patches.
    /// </summary>
    Task<PatchCoverageReadModel> GetAsync(CancellationToken ct);
}
