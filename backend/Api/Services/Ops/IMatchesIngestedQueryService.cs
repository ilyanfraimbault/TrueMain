using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Allowed x-axis granularities for the ingestion-throughput series. Narrower than
/// <see cref="MatchTimeGranularity"/> on purpose: <c>Patch</c> is a property of the
/// games, not of when we ingested them, and <c>Year</c> cannot fill more than one
/// bucket under the 180-day run retention.
/// </summary>
public enum IngestionTimeGranularity
{
    Day,
    Week,
    Month
}

public interface IMatchesIngestedQueryService
{
    Task<MatchesIngestedReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct);
}
