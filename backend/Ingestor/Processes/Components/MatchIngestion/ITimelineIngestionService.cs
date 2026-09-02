using Core.Lol.Identifiers;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface ITimelineIngestionService
{
    /// <summary>
    /// Downloads the timelines the account still owes us. Riot calls plus the
    /// pending-timeline read only, so the caller runs it outside its transaction
    /// (#264, #1229); the payloads are materialised for <see cref="WriteAsync"/>.
    /// </summary>
    Task<TimelineIngestionPlan> PrepareAsync(
        IDataSession session,
        RegionalRoute region,
        IReadOnlyCollection<string> allMatchIds,
        IReadOnlyCollection<string> newMatchIds,
        CancellationToken ct);

    /// <summary>
    /// Applies a plan produced by <see cref="PrepareAsync"/> and returns how many
    /// matches were actually updated. Writes only.
    /// </summary>
    Task<int> WriteAsync(
        IDataSession session,
        TimelineIngestionPlan plan,
        int saveBatchSize,
        CancellationToken ct);
}

/// <summary>
/// Timelines downloaded for one account, bounded by the account's match-v5 page
/// (<c>MatchIngestion:MatchesPerAccount</c>), so the in-memory hop between the fetch
/// and the write phase stays a few tens of MB at worst.
/// </summary>
public sealed record TimelineIngestionPlan(IReadOnlyList<FetchedTimeline> Timelines);

public sealed record FetchedTimeline(string MatchId, MatchTimelineDto Timeline);
