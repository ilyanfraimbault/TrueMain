using Core.Lol.Map;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// Read path for the admin data-quality panel's incomplete-matches list: matches with
/// incomplete/inconsistent data, grouped by the check they trip (queue-scoped, see
/// <see cref="MatchDataQualityRules"/>). Read-only diagnostics — no repair.
/// </summary>
public interface IIncompleteMatchesQueryService
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
}

/// <summary>
/// Lists the matches the data-quality checks flag. The per-match facts (participant
/// count, per-team position/champion shape) are computed in the database; the (cheap,
/// fixed-size) rule evaluation runs in memory over the candidate window so each check
/// stays independently listable.
/// </summary>
public sealed class IncompleteMatchesQueryService(TrueMainDbContext db) : IIncompleteMatchesQueryService
{
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
        // Upper bound keeps `(page - 1) * pageSize` within int range even at the
        // maximum page size, mirroring ProcessRunsQueryService.
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        var issueFilter = MatchDataQualityRules.ParseIssue(issue);

        var now = DateTime.UtcNow;
        var staleTimelineCutoff = MatchDataQualityRules.StaleTimelineCutoff(now);
        // Age floor: a match must be at least this old to be considered at all.
        DateTime? ageCutoff = minAgeHours is > 0 ? now.AddHours(-minAgeHours.Value) : null;

        var candidates = await LoadCandidatesAsync(queueId, ageCutoff, staleTimelineCutoff, ct);

        // Evaluate every check per match, in memory, against the candidate window.
        var flagged = new List<FlaggedMatch>();
        foreach (var candidate in candidates)
        {
            var issues = MatchDataQualityRules.Evaluate(candidate, staleTimelineCutoff);
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
            StaleTimelineThresholdHours = MatchDataQualityRules.StaleTimelineThresholdHours
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
        var profiledQueueIds = QueueDataQualityProfile.KnownQueueIds;
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
                IssueType = MatchDataQualityRules.ToWireName(issueType),
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
            Issues = flagged.Issues.Select(i => MatchDataQualityRules.ToWireName(i)).ToList()
        };
    }

    // ---- internal projections ------------------------------------------------

    private sealed record MatchHeader(
        string Id,
        string PlatformId,
        int QueueId,
        DateTime GameStartTimeUtc,
        int GameDurationSeconds,
        bool TimelineIngested);

    private sealed record FlaggedMatch(MatchSummary Match, IReadOnlyList<DataQualityIssueType> Issues);
}
