using Data.Repositories;

namespace Ingestor.Processes.Components.Coverage;

/// <summary>
/// Builds the shared per-champion coverage snapshot consumed by both scoring (A)
/// and main classification (C). One signal, two consumers.
/// </summary>
public interface IChampionCoverageProvider
{
    Task<ChampionCoverageSnapshot> GetSnapshotAsync(IDataSession session, CancellationToken ct);
}
