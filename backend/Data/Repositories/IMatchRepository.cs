using Data.Entities;

namespace Data.Repositories;

public interface IMatchRepository
{
    Task<HashSet<string>> GetExistingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    Task<HashSet<string>> GetTimelinePendingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    Task SetTimelineIngestedAsync(string matchId, bool timelineIngested, CancellationToken ct);

    /// <summary>Queue + raw game version of a match, or null if it is not stored.</summary>
    Task<MatchAggregationInfo?> GetAggregationInfoAsync(string matchId, CancellationToken ct);

    /// <summary>
    /// Atomically claims a match for timeline aggregation: flips TimelineAggregated
    /// false → true in a single conditional update and returns whether this caller
    /// won the flip. The row lock serialises the same match ingested from multiple
    /// accounts, so exactly one caller folds it into the add-only aggregates.
    /// </summary>
    Task<bool> TryClaimTimelineAggregationAsync(string matchId, CancellationToken ct);

    void Add(Match match);
}

/// <summary>Minimal match scope needed to fold a timeline into the aggregates.</summary>
public sealed record MatchAggregationInfo(int QueueId, string GameVersion);
