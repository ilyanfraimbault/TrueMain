using Core;
using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface ITimelineIngestionService
{
    Task<int> IngestTimelinesAsync(
        IDataSession session,
        RegionalRoute region,
        IReadOnlyCollection<string> allMatchIds,
        IReadOnlyCollection<string> newMatchIds,
        int saveBatchSize,
        CancellationToken ct);
}
