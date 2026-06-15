using Core.Lol.Identifiers;
using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface IMatchSnapshotWriter
{
    Task<SnapshotIngestionResult> IngestSnapshotsAsync(
        IDataSession session,
        string platformId,
        string puuid,
        RegionalRoute region,
        int matchesPerAccount,
        int saveBatchSize,
        int maxFetchConcurrency,
        CancellationToken ct);
}

public sealed record SnapshotIngestionResult(
    IReadOnlyCollection<string> AllMatchIds,
    IReadOnlyCollection<string> NewMatchIds,
    int Inserted,
    int Skipped);
