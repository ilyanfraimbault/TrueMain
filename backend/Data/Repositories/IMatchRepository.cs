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
    void Add(Match match);
}
