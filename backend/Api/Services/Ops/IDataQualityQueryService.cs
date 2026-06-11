using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the admin data-quality panel: surfaces matches with
/// incomplete/inconsistent data (queue-scoped) and the per-match breakdown.
/// Read-only diagnostics — no repair. Projects read-models with
/// <c>AsNoTracking</c>; no generic repository.
/// </summary>
public interface IDataQualityQueryService
{
    /// <summary>
    /// Flagged matches grouped by issue type, paged and filterable.
    /// </summary>
    /// <param name="issue">
    /// Restrict to a single <see cref="DataQualityIssueType"/> (case-insensitive
    /// name); null/blank/unknown means all checks.
    /// </param>
    /// <param name="queueId">Restrict to one queue id; null means all queues.</param>
    /// <param name="minAgeHours">
    /// Only consider matches whose <c>GameStartTimeUtc</c> is at least this many
    /// hours old; null means no age floor.
    /// </param>
    /// <param name="page">1-based page index (clamped to ≥ 1).</param>
    /// <param name="pageSize">Per-issue sample size (clamped to a safe range).</param>
    /// <param name="ct">Request cancellation token.</param>
    Task<IncompleteMatchesReadModel> GetIncompleteMatchesAsync(
        string? issue,
        int? queueId,
        int? minAgeHours,
        int? page,
        int? pageSize,
        CancellationToken ct);

    /// <summary>
    /// Per-match detail: both teams by position with the gaps identified, plus
    /// the issue types the match trips. Null when no such match exists.
    /// </summary>
    Task<MatchDataQualityDetailReadModel?> GetMatchDetailAsync(string matchId, CancellationToken ct);
}
