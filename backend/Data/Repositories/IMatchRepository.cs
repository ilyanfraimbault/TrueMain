using Data.Entities;

namespace Data.Repositories;

public interface IMatchRepository
{
    Task<HashSet<string>> GetExistingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    Task<HashSet<string>> GetTimelinePendingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    /// <summary>
    /// Flags the timeline state of a whole batch of matches in one statement (#1229).
    /// </summary>
    Task SetTimelineIngestedAsync(IReadOnlyCollection<string> matchIds, bool timelineIngested, CancellationToken ct);
    /// <summary>
    /// Each match's duration in seconds, for a whole batch in one statement; matches
    /// that do not exist are absent from the result.
    /// </summary>
    Task<Dictionary<string, int>> GetGameDurationSecondsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    void Add(Match match);
}
