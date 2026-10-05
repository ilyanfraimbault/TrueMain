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

    /// <summary>
    /// Flips <see cref="Match.PaceBenchmarkAggregated"/> for those of <paramref name="matchIds"/>
    /// that are not folded yet and returns what the fold needs of each (#1912). A conditional
    /// update rather than a read then a write: a concurrent transaction holding the same match
    /// waits on the row lock, re-checks the condition and gets nothing back, so a match is
    /// never counted twice.
    /// </summary>
    Task<List<PaceBenchmarkFoldClaim>> ClaimPaceBenchmarkFoldAsync(
        IReadOnlyCollection<string> matchIds,
        CancellationToken ct);
}

/// <summary>A match claimed for the pace benchmark fold, with the columns the fold reads.</summary>
public sealed record PaceBenchmarkFoldClaim(
    string Id,
    string? Patch,
    DateTime GameStartTimeUtc,
    int GameDurationSeconds);
