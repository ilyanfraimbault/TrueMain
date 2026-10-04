using Core.Lol.Map;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The per-match data-quality checks, shared by the incomplete-matches list and the
/// per-match drill-down so both agree on what a match trips. Pure: it judges a
/// <see cref="MatchSummary"/> and never touches the database. Each check is
/// queue-scoped via <see cref="QueueDataQualityProfile"/> so non-applicable rules
/// (e.g. lanes on ARAM) can't flood the panel with false positives:
/// <list type="bullet">
///   <item><b>MissingTimeline</b> — <c>TimelineIngested = false</c> AND the game
///     is older than <see cref="StaleTimelineThresholdHours"/>, so the normal
///     pending backlog isn't reported as stuck. Queue-agnostic.</item>
///   <item><b>WrongParticipantCount</b> — row count ≠ the queue's expected count.
///     Known queues only.</item>
///   <item><b>MissingTeamPosition</b> — a team missing one of the five lanes.
///     Lane queues only.</item>
///   <item><b>ZeroDuration</b> — <c>GameDurationSeconds = 0</c>. Queue-agnostic.</item>
///   <item><b>DuplicateChampion</b> — same champion twice on one team. Lane
///     queues only (a team is a defined champion set there).</item>
/// </list>
/// </summary>
internal static class MatchDataQualityRules
{
    /// <summary>
    /// A <c>TimelineIngested = false</c> match younger than this is treated as
    /// normally pending the recovery job, not stuck — so it isn't flagged.
    /// </summary>
    public const int StaleTimelineThresholdHours = 6;

    /// <summary>
    /// The staleness cutoff for the missing-timeline check, computed the same way by
    /// the list and the detail so they agree on whether the timeline gap is "stuck".
    /// </summary>
    public static DateTime StaleTimelineCutoff(DateTime nowUtc)
        => nowUtc.AddHours(-StaleTimelineThresholdHours);

    public static List<DataQualityIssueType> Evaluate(MatchSummary match, DateTime staleTimelineCutoff)
    {
        var issues = new List<DataQualityIssueType>();
        var profile = match.Profile;

        // Missing timeline — queue-agnostic, age-gated so the normal pending
        // backlog isn't reported as stuck.
        if (!match.TimelineIngested && match.GameStartTimeUtc <= staleTimelineCutoff)
        {
            issues.Add(DataQualityIssueType.MissingTimeline);
        }

        // Zero duration — queue-agnostic.
        if (match.GameDurationSeconds <= 0)
        {
            issues.Add(DataQualityIssueType.ZeroDuration);
        }

        // Count/position rules only make sense for known queues.
        if (profile.IsKnown)
        {
            if (match.ParticipantCount != profile.ExpectedParticipants)
            {
                issues.Add(DataQualityIssueType.WrongParticipantCount);
            }

            if (profile.HasLanes)
            {
                // A lane-queue team should carry all five distinct lanes. Only
                // assert this on teams that have the expected per-team headcount,
                // so a wrong-count match is reported as wrong-count, not as a
                // cascade of phantom missing positions.
                var expectedPerTeam = profile.ExpectedParticipants / profile.TeamCount;
                if (match.AnyTeamMissingLane(expectedPerTeam))
                {
                    issues.Add(DataQualityIssueType.MissingTeamPosition);
                }

                if (match.AnyTeamDuplicateChampion())
                {
                    issues.Add(DataQualityIssueType.DuplicateChampion);
                }
            }
        }

        return issues;
    }

    public static DataQualityIssueType? ParseIssue(string? issue)
        => Enum.TryParse<DataQualityIssueType>(issue?.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : null;

    // camelCase the enum name to match the API's global JSON policy.
    public static string ToWireName(DataQualityIssueType issue)
    {
        var name = issue.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

/// <summary>Per-(match, team) shape the rules inspect.</summary>
internal sealed record TeamShape(
    string MatchId,
    int TeamId,
    int ParticipantCount,
    int DistinctLanePositions,
    int DistinctChampions);

/// <summary>One participant row as the per-match drill-down reads it.</summary>
internal sealed record ParticipantRow(
    int ParticipantId,
    int TeamId,
    string TeamPosition,
    int ChampionId,
    string SummonerName,
    bool Win);

/// <summary>
/// The flattened per-match facts the rules need, plus the resolved queue
/// profile. Built from the DB-side team shapes (list path) or from raw
/// participants (detail path) so both evaluate identical logic.
/// </summary>
internal sealed class MatchSummary
{
    public string MatchId { get; private init; } = string.Empty;
    public string PlatformId { get; private init; } = string.Empty;
    public int QueueId { get; private init; }
    public DateTime GameStartTimeUtc { get; private init; }
    public int GameDurationSeconds { get; private init; }
    public bool TimelineIngested { get; private init; }
    public int ParticipantCount { get; private init; }
    public QueueDataQualityProfile Profile { get; private init; } = QueueDataQualityProfile.Unknown;

    private IReadOnlyList<TeamShape> Teams { get; init; } = [];

    public static MatchSummary From(
        string matchId,
        string platformId,
        int queueId,
        DateTime gameStartTimeUtc,
        int gameDurationSeconds,
        bool timelineIngested,
        IReadOnlyList<TeamShape> teams,
        QueueDataQualityProfile profile) => new()
        {
            MatchId = matchId,
            PlatformId = platformId,
            QueueId = queueId,
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = gameDurationSeconds,
            TimelineIngested = timelineIngested,
            ParticipantCount = teams.Sum(t => t.ParticipantCount),
            Teams = teams,
            Profile = profile
        };

    // Detail-path overload: derive team shapes from raw participant rows so
    // the per-team rule inputs match the DB-side GROUP BY exactly. The header
    // facts (start time, duration, timeline flag) are passed through so this
    // summary is complete — Evaluate reads them off the summary, so leaving them
    // at C# defaults would silently flag every detail match as MissingTimeline +
    // ZeroDuration.
    public static MatchSummary From(
        string matchId,
        string platformId,
        int queueId,
        DateTime gameStartTimeUtc,
        int gameDurationSeconds,
        bool timelineIngested,
        IReadOnlyList<ParticipantRow> participants,
        QueueDataQualityProfile profile)
    {
        var teams = participants
            .GroupBy(p => p.TeamId)
            .Select(g => new TeamShape(
                matchId,
                g.Key,
                g.Count(),
                g.Select(p => p.TeamPosition)
                    .Where(pos => !string.IsNullOrEmpty(pos))
                    .Distinct()
                    .Count(),
                g.Select(p => p.ChampionId).Distinct().Count()))
            .ToList();

        return new MatchSummary
        {
            MatchId = matchId,
            PlatformId = platformId,
            QueueId = queueId,
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = gameDurationSeconds,
            TimelineIngested = timelineIngested,
            ParticipantCount = participants.Count,
            Teams = teams,
            Profile = profile
        };
    }

    /// <summary>
    /// True when any full-size team is missing at least one lane (its distinct
    /// lane-position count is below the expected per-team headcount).
    /// </summary>
    public bool AnyTeamMissingLane(int expectedPerTeam)
        => Teams.Any(t => t.ParticipantCount == expectedPerTeam
            && t.DistinctLanePositions < expectedPerTeam);

    /// <summary>True when any team has a champion appearing more than once.</summary>
    public bool AnyTeamDuplicateChampion()
        => Teams.Any(t => t.DistinctChampions < t.ParticipantCount);
}
