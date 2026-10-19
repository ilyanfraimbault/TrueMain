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

    /// <summary>
    /// Sends back to the champion pattern aggregation every match in which one of
    /// <paramref name="mains"/> played that champion on that platform (#1367). Main-ness is
    /// stored on the aggregate slice and decides whether a mains-only run folds the slice at
    /// all, so a slice whose player was just promoted or demoted has to be rebuilt.
    /// </summary>
    Task<int> InvalidatePatternAggregationAsync(
        IReadOnlyCollection<MainChampionKey> mains,
        CancellationToken ct);
}

/// <summary>The key of a <c>main_champion_stats</c> row: one account's champion on one platform.</summary>
public readonly record struct MainChampionKey(string PlatformId, string Puuid, int ChampionId);

/// <summary>A match claimed for the pace benchmark fold, with the columns the fold reads.</summary>
public sealed record PaceBenchmarkFoldClaim(
    string Id,
    string? Patch,
    DateTime GameStartTimeUtc,
    int GameDurationSeconds,
    bool EndedInEarlySurrender);
