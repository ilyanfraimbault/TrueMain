using Core.Lol.Map;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the data-quality panel. Each check is queue-scoped via
/// <see cref="QueueDataQualityProfile"/> so non-applicable rules (e.g. lanes on
/// ARAM) can't flood the panel with false positives:
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
/// The per-match facts (participant count, per-team position/champion shape) are
/// computed in the database; the (cheap, fixed-size) rule evaluation runs in
/// memory over the candidate window so each check stays independently listable.
/// </summary>
public sealed class DataQualityQueryService(TrueMainDbContext db) : IDataQualityQueryService
{
    /// <summary>
    /// A <c>TimelineIngested = false</c> match younger than this is treated as
    /// normally pending the recovery job, not stuck — so it isn't flagged.
    /// </summary>
    private const int StaleTimelineThresholdHours = 6;

    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    // Upper bound on flagged matches materialised per request. Unlike a raw
    // newest-first window, this caps the set *after* the rule predicates have run
    // in the database, so it bounds how many genuinely-broken matches we load
    // (not how far back we look). Old stuck matches stay reachable because the
    // staleness/shape predicates filter before this Take.
    private const int CandidateScanLimit = 5000;

    public async Task<IncompleteMatchesReadModel> GetIncompleteMatchesAsync(
        string? issue,
        int? queueId,
        int? minAgeHours,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        var issueFilter = ParseIssue(issue);

        var now = DateTime.UtcNow;
        var staleTimelineCutoff = now.AddHours(-StaleTimelineThresholdHours);
        // Age floor: a match must be at least this old to be considered at all.
        DateTime? ageCutoff = minAgeHours is > 0 ? now.AddHours(-minAgeHours.Value) : null;

        var candidates = await LoadCandidatesAsync(queueId, ageCutoff, staleTimelineCutoff, ct);

        // Evaluate every check per match, in memory, against the candidate window.
        var flagged = new List<FlaggedMatch>();
        foreach (var candidate in candidates)
        {
            var issues = Evaluate(candidate, staleTimelineCutoff);
            if (issues.Count == 0)
            {
                continue;
            }

            // When filtering to a single issue, drop matches that don't trip it.
            if (issueFilter is not null && !issues.Contains(issueFilter.Value))
            {
                continue;
            }

            flagged.Add(new FlaggedMatch(candidate, issues));
        }

        // Distinct flagged-match count (a match tripping several checks counts once).
        var total = flagged.Count;

        // One group per issue type that's both in scope (matches the filter) and
        // actually has flagged matches, newest-first, sampled to the page size.
        var groups = BuildGroups(flagged, issueFilter, effectivePage, effectivePageSize);

        return new IncompleteMatchesReadModel
        {
            Groups = groups,
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize,
            StaleTimelineThresholdHours = StaleTimelineThresholdHours
        };
    }

    public async Task<MatchDataQualityDetailReadModel?> GetMatchDetailAsync(string matchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matchId))
        {
            return null;
        }

        var trimmedId = matchId.Trim();

        var match = await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == trimmedId)
            .Select(m => new
            {
                m.Id,
                m.PlatformId,
                m.QueueId,
                m.GameMode,
                m.GameStartTimeUtc,
                m.GameDurationSeconds,
                m.GameVersion,
                m.TimelineIngested
            })
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            return null;
        }

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.MatchId == trimmedId)
            .OrderBy(p => p.TeamId)
            .ThenBy(p => p.ParticipantId)
            .Select(p => new ParticipantRow(
                p.ParticipantId,
                p.TeamId,
                p.TeamPosition,
                p.ChampionId,
                p.SummonerName,
                p.Win))
            .ToListAsync(ct);

        var profile = QueueDataQualityProfile.ForQueue(match.QueueId);
        var summary = MatchSummary.From(match.Id, match.QueueId, participants, profile);
        var issues = Evaluate(
            summary,
            // Recompute the same staleness cutoff used by the list so detail and
            // list agree on whether the timeline gap is "stuck".
            DateTime.UtcNow.AddHours(-StaleTimelineThresholdHours),
            match.TimelineIngested,
            match.GameStartTimeUtc,
            match.GameDurationSeconds);

        var teams = BuildTeams(participants, profile);

        return new MatchDataQualityDetailReadModel
        {
            MatchId = match.Id,
            PlatformId = match.PlatformId,
            QueueId = match.QueueId,
            GameMode = match.GameMode,
            GameStartTimeUtc = match.GameStartTimeUtc,
            GameDurationSeconds = match.GameDurationSeconds,
            GameVersion = match.GameVersion,
            TimelineIngested = match.TimelineIngested,
            ParticipantCount = summary.ParticipantCount,
            ExpectedParticipantCount = profile.IsKnown ? profile.ExpectedParticipants : null,
            QueueKnown = profile.IsKnown,
            HasLanes = profile.HasLanes,
            Issues = issues.Select(i => ToWireName(i)).ToList(),
            Teams = teams
        };
    }

    // ---- candidate loading ---------------------------------------------------

    private async Task<IReadOnlyList<MatchSummary>> LoadCandidatesAsync(
        int? queueId,
        DateTime? ageCutoff,
        DateTime staleTimelineCutoff,
        CancellationToken ct)
    {
        // Default to ALL queues: the queue-agnostic checks (missing-timeline,
        // zero-duration) must be able to flag matches from an unknown/new queue,
        // and Evaluate already skips the profile-dependent checks for those. When
        // a specific queue is requested, scope to it.
        var baseQuery = db.Matches.AsNoTracking();
        if (queueId is { } requested)
        {
            baseQuery = baseQuery.Where(m => m.QueueId == requested);
        }

        if (ageCutoff is not null)
        {
            baseQuery = baseQuery.Where(m => m.GameStartTimeUtc <= ageCutoff.Value);
        }

        // Two independent candidate sets, each capped on its OWN newest-first
        // window, then unioned. Splitting them is what makes old stuck matches
        // reachable: the header-only checks reduce to indexed predicates, so their
        // window only ever contains genuinely-broken matches — an OLD stuck match
        // surfaces even behind an arbitrary number of newer HEALTHY ones. The
        // shape window can't be reduced to a single predicate, so it scans the
        // newest profiled-queue matches; saturating it with healthy matches can't
        // crowd out the header-flagged set because they're capped separately.
        //
        //  (a) header-flagged: missing-timeline (age-gated) OR zero-duration —
        //      queue-agnostic, exact predicate.
        var headerFlagged = baseQuery
            .Where(m =>
                (!m.TimelineIngested && m.GameStartTimeUtc <= staleTimelineCutoff)
                || m.GameDurationSeconds <= 0);

        //  (b) shape candidates: profiled-queue matches whose per-team shape the
        //      in-memory Evaluate inspects for wrong-count / missing-lane /
        //      duplicate-champion. Only profiled queues carry those rules.
        var profiledQueueIds = QueueDataQualityProfile.KnownQueueIds.ToArray();
        var shapeCandidates = baseQuery
            .Where(m => profiledQueueIds.Contains(m.QueueId));

        var headerHeaders = await TakeNewestHeadersAsync(headerFlagged, ct);
        var shapeHeaders = await TakeNewestHeadersAsync(shapeCandidates, ct);

        var matchHeaders = headerHeaders
            .Concat(shapeHeaders)
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .ToList();

        if (matchHeaders.Count == 0)
        {
            return [];
        }

        var matchIds = matchHeaders.Select(m => m.Id).ToList();

        // Per-(match, team) shape: participant count, distinct lane positions and
        // whether any champion repeats on the team. Computed in the database with
        // a GROUP BY so we never pull every participant row for the whole window.
        var teamShapes = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .GroupBy(p => new { p.MatchId, p.TeamId })
            .Select(g => new TeamShape(
                g.Key.MatchId,
                g.Key.TeamId,
                g.Count(),
                // Distinct non-empty lane positions present on the team.
                g.Select(p => p.TeamPosition)
                    .Where(pos => pos != null && pos != "")
                    .Distinct()
                    .Count(),
                // Distinct champions vs participant count: fewer distinct => a
                // champion repeats on the team.
                g.Select(p => p.ChampionId).Distinct().Count()))
            .ToListAsync(ct);

        var shapesByMatch = teamShapes
            .GroupBy(s => s.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return matchHeaders
            .Select(header =>
            {
                shapesByMatch.TryGetValue(header.Id, out var shapes);
                var profile = QueueDataQualityProfile.ForQueue(header.QueueId);
                return MatchSummary.From(
                    header.Id,
                    header.PlatformId,
                    header.QueueId,
                    header.GameStartTimeUtc,
                    header.GameDurationSeconds,
                    header.TimelineIngested,
                    shapes ?? [],
                    profile);
            })
            .ToList();
    }

    // Newest-first, capped projection of match headers for one candidate query.
    private static async Task<List<MatchHeader>> TakeNewestHeadersAsync(
        IQueryable<Data.Entities.Match> query,
        CancellationToken ct)
        => await query
            .OrderByDescending(m => m.GameStartTimeUtc)
            .ThenByDescending(m => m.Id)
            .Take(CandidateScanLimit)
            .Select(m => new MatchHeader(
                m.Id,
                m.PlatformId,
                m.QueueId,
                m.GameStartTimeUtc,
                m.GameDurationSeconds,
                m.TimelineIngested))
            .ToListAsync(ct);

    // ---- rule evaluation -----------------------------------------------------

    private static IReadOnlyList<DataQualityIssueType> Evaluate(MatchSummary match, DateTime staleTimelineCutoff)
        => Evaluate(
            match,
            staleTimelineCutoff,
            match.TimelineIngested,
            match.GameStartTimeUtc,
            match.GameDurationSeconds);

    private static List<DataQualityIssueType> Evaluate(
        MatchSummary match,
        DateTime staleTimelineCutoff,
        bool timelineIngested,
        DateTime gameStartTimeUtc,
        int gameDurationSeconds)
    {
        var issues = new List<DataQualityIssueType>();
        var profile = match.Profile;

        // Missing timeline — queue-agnostic, age-gated so the normal pending
        // backlog isn't reported as stuck.
        if (!timelineIngested && gameStartTimeUtc <= staleTimelineCutoff)
        {
            issues.Add(DataQualityIssueType.MissingTimeline);
        }

        // Zero duration — queue-agnostic.
        if (gameDurationSeconds <= 0)
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

    // ---- grouping / paging ---------------------------------------------------

    private static IReadOnlyList<DataQualityIssueGroupReadModel> BuildGroups(
        IReadOnlyList<FlaggedMatch> flagged,
        DataQualityIssueType? issueFilter,
        int page,
        int pageSize)
    {
        // Stable issue-type order = enum declaration order.
        var issueTypes = issueFilter is { } single
            ? [single]
            : Enum.GetValues<DataQualityIssueType>();

        var groups = new List<DataQualityIssueGroupReadModel>();
        foreach (var issueType in issueTypes)
        {
            // Matches tripping this check, newest-first.
            var matchesForIssue = flagged
                .Where(f => f.Issues.Contains(issueType))
                .OrderByDescending(f => f.Match.GameStartTimeUtc)
                .ThenByDescending(f => f.Match.MatchId, StringComparer.Ordinal)
                .ToList();

            if (matchesForIssue.Count == 0)
            {
                continue;
            }

            var sample = matchesForIssue
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => ToFlaggedReadModel(f))
                .ToList();

            groups.Add(new DataQualityIssueGroupReadModel
            {
                IssueType = ToWireName(issueType),
                Count = matchesForIssue.Count,
                Matches = sample
            });
        }

        return groups;
    }

    private static FlaggedMatchReadModel ToFlaggedReadModel(FlaggedMatch flagged)
    {
        var match = flagged.Match;
        return new FlaggedMatchReadModel
        {
            MatchId = match.MatchId,
            PlatformId = match.PlatformId,
            QueueId = match.QueueId,
            GameStartTimeUtc = match.GameStartTimeUtc,
            GameDurationSeconds = match.GameDurationSeconds,
            TimelineIngested = match.TimelineIngested,
            ParticipantCount = match.ParticipantCount,
            ExpectedParticipantCount = match.Profile.IsKnown ? match.Profile.ExpectedParticipants : null,
            Issues = flagged.Issues.Select(i => ToWireName(i)).ToList()
        };
    }

    // ---- detail team layout --------------------------------------------------

    private static IReadOnlyList<MatchTeamReadModel> BuildTeams(
        IReadOnlyList<ParticipantRow> participants,
        QueueDataQualityProfile profile)
    {
        var presentTeamIds = participants.Select(p => p.TeamId).Distinct().ToList();

        // For a known two-team queue (SR/ARAM) always surface BOTH standard team
        // ids, even when a team has zero ingested rows — otherwise a half-missing
        // match would hide its absent team entirely, and the operator couldn't see
        // that team's missing lane slots. Any non-standard team ids actually
        // present (odd data) are appended after.
        var includeBothStandardTeams = profile.IsKnown && profile.TeamCount == 2;
        var teamIds = (includeBothStandardTeams
                ? QueueDataQualityProfile.StandardTeamIds
                : QueueDataQualityProfile.StandardTeamIds.Where(presentTeamIds.Contains))
            .Concat(presentTeamIds.Where(id => !QueueDataQualityProfile.StandardTeamIds.Contains(id)))
            .ToList();

        if (teamIds.Count == 0)
        {
            return [];
        }

        var teams = new List<MatchTeamReadModel>();
        foreach (var teamId in teamIds)
        {
            var members = participants.Where(p => p.TeamId == teamId).ToList();

            // Champions that appear more than once on this team (duplicate signal).
            var duplicateChampions = members
                .GroupBy(m => m.ChampionId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            List<MatchSlotReadModel> slots;
            if (profile.HasLanes)
            {
                // Lay the team across the five canonical lanes, flagging gaps and
                // any participant whose champion repeats on the team.
                slots = QueueDataQualityProfile.LanePositions
                    .Select(position =>
                    {
                        var occupant = members.FirstOrDefault(m =>
                            string.Equals(m.TeamPosition, position, StringComparison.OrdinalIgnoreCase));
                        return occupant is null
                            ? new MatchSlotReadModel { Position = position, Filled = false }
                            : new MatchSlotReadModel
                            {
                                Position = position,
                                Filled = true,
                                ParticipantId = occupant.ParticipantId,
                                ChampionId = occupant.ChampionId,
                                SummonerName = occupant.SummonerName,
                                Win = occupant.Win,
                                DuplicateChampion = duplicateChampions.Contains(occupant.ChampionId)
                            };
                    })
                    .ToList();

                // Surface any participant with an unrecognised/empty TeamPosition
                // that didn't map onto a canonical lane, so laned-queue data with
                // a bad position isn't silently dropped from the layout.
                var placed = slots
                    .Where(s => s.ParticipantId is not null)
                    .Select(s => s.ParticipantId!.Value)
                    .ToHashSet();
                slots.AddRange(members
                    .Where(m => !placed.Contains(m.ParticipantId))
                    .Select(m => new MatchSlotReadModel
                    {
                        Position = string.IsNullOrWhiteSpace(m.TeamPosition) ? "UNKNOWN" : m.TeamPosition,
                        Filled = true,
                        ParticipantId = m.ParticipantId,
                        ChampionId = m.ChampionId,
                        SummonerName = m.SummonerName,
                        Win = m.Win,
                        DuplicateChampion = duplicateChampions.Contains(m.ChampionId)
                    }));
            }
            else
            {
                // Laneless queue: one slot per participant, in roster order.
                slots = members
                    .Select(m => new MatchSlotReadModel
                    {
                        Position = string.Empty,
                        Filled = true,
                        ParticipantId = m.ParticipantId,
                        ChampionId = m.ChampionId,
                        SummonerName = m.SummonerName,
                        Win = m.Win,
                        DuplicateChampion = duplicateChampions.Contains(m.ChampionId)
                    })
                    .ToList();
            }

            teams.Add(new MatchTeamReadModel { TeamId = teamId, Slots = slots });
        }

        return teams;
    }

    // ---- helpers -------------------------------------------------------------

    private static DataQualityIssueType? ParseIssue(string? issue)
        => Enum.TryParse<DataQualityIssueType>(issue?.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : null;

    // camelCase the enum name to match the API's global JSON policy.
    private static string ToWireName(DataQualityIssueType issue)
    {
        var name = issue.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    // ---- internal projections ------------------------------------------------

    private sealed record MatchHeader(
        string Id,
        string PlatformId,
        int QueueId,
        DateTime GameStartTimeUtc,
        int GameDurationSeconds,
        bool TimelineIngested);

    private sealed record TeamShape(
        string MatchId,
        int TeamId,
        int ParticipantCount,
        int DistinctLanePositions,
        int DistinctChampions);

    private sealed record ParticipantRow(
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
    private sealed class MatchSummary
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
        // the per-team rule inputs match the DB-side GROUP BY exactly.
        public static MatchSummary From(
            string matchId,
            int queueId,
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
                QueueId = queueId,
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

    private sealed record FlaggedMatch(MatchSummary Match, IReadOnlyList<DataQualityIssueType> Issues);
}
