using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Read path for the truemain match-history feed
/// (<c>GET /truemains/{nameTag}/matches</c>): resolves the account, pages the
/// player's matches, and hands the page's (match, participant) keys to the
/// shared <see cref="MatchSummaryHydrator"/> — which grades every participant
/// with the same scorer as the single-match detail page, so a row's score,
/// placement and MVP/ACE badge can never disagree with what expanding it shows.
/// </summary>
public sealed class MatchSummariesQueryService(
    TrueMainDbContext db,
    MatchSummaryHydrator hydrator,
    ILogger<MatchSummariesQueryService> logger) : IMatchSummariesQueryService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;
    private const string Surface = "match-summaries";

    public async Task<MatchSummariesResponse?> GetAsync(
        string nameTag,
        int page,
        int pageSize,
        string? position,
        int? championId,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        // Normalize the position filter once. The DB stores team positions
        // as upper-case Riot strings (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY);
        // any other value clamps to null so a bogus query param doesn't
        // wedge the comparison.
        var normalizedPosition = string.IsNullOrWhiteSpace(position)
            ? null
            : position.Trim().ToUpperInvariant();
        if (normalizedPosition is not null
            && normalizedPosition != "TOP"
            && normalizedPosition != "JUNGLE"
            && normalizedPosition != "MIDDLE"
            && normalizedPosition != "BOTTOM"
            && normalizedPosition != "UTILITY")
        {
            normalizedPosition = null;
        }

        var championFilter = championId is > 0 ? championId : null;

        // Multi-platform name-tag disambiguation: a (gameName, tagLine) pair
        // is unique within a Riot routing region but can collide across
        // regions. Picking the most-recently-active row keeps this endpoint
        // and `/truemains/{nameTag}/profile` (ProfileQueryService) aligned —
        // both routes always resolve to the same account for a given name
        // tag, so the user never lands on inconsistent profile vs. matches.
        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new { a.Id, a.Puuid })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var clampedPageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var clampedPage = page < 1 ? 1 : page;

        // Total first so the frontend can render the pagination control even
        // when it lands directly on a deep page via the URL. Filtered to the
        // same predicate as the data query — we never want the count and the
        // list to disagree about which matches "belong to" this player.
        //
        // The ingestor now pulls match ids with `type=ranked` at the Riot
        // source so nothing non-ranked enters the DB going forward. But
        // historical CHERRY (Arena) rows pre-date that change and still
        // live in `matches` — we keep them on disk on purpose (no
        // destructive cleanup) but exclude them from the aggregations and
        // visible feeds so Arena rounds don't pollute a user's "what
        // games have I played" view. The other aggregation surface —
        // MainChampionStat — already filters to QueueId=420 in
        // MainStatsCalculator, so the sidebar mains / role distribution
        // were never affected.
        //
        // Position / champion filters live on the same `Any(...)` clause so
        // the count and the page slice share a single predicate. Both apply
        // to the self participant in the match — `p.Puuid == account.Puuid`
        // narrows to that row, and the optional extras filter on its
        // championId / teamPosition.
        var matchesQuery = db.Matches
            .AsNoTracking()
            .Where(m => m.GameMode != "CHERRY")
            .Where(m => m.Participants.Any(p =>
                p.Puuid == account.Puuid
                && (championFilter == null || p.ChampionId == championFilter)
                && (normalizedPosition == null || p.TeamPosition == normalizedPosition)));

        var total = await matchesQuery.CountAsync(ct);
        if (total == 0)
        {
            // Same clamped page as the past-end-page branch below, so an empty
            // result never disagrees with an out-of-range one about which page
            // it's reporting (#222).
            return new MatchSummariesResponse
            {
                Matches = Array.Empty<MatchSummaryReadModel>(),
                Page = clampedPage,
                PageSize = clampedPageSize,
                Total = 0,
            };
        }

        // The page slice carries the self participant id straight out of the
        // ordering query, so hydration needs no second lookup to know whose
        // slice of each match the row renders.
        var pageKeys = await matchesQuery
            .OrderByDescending(m => m.GameStartTimeUtc)
            .Skip((clampedPage - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .Select(m => new
            {
                MatchId = m.Id,
                ParticipantId = m.Participants
                    .Where(p => p.Puuid == account.Puuid)
                    .Select(p => (int?)p.ParticipantId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        if (pageKeys.Count == 0)
        {
            // Requested page is past the last one. Return an empty page with
            // the real total so the frontend's pagination control still
            // resolves to a valid range.
            return new MatchSummariesResponse
            {
                Matches = Array.Empty<MatchSummaryReadModel>(),
                Page = clampedPage,
                PageSize = clampedPageSize,
                Total = total,
            };
        }

        var keys = new List<MatchSummaryKey>(pageKeys.Count);
        foreach (var row in pageKeys)
        {
            if (row.ParticipantId is null)
            {
                logger.LogWarning(
                    "{Surface} match missing self participant match_id={MatchId} puuid={Puuid}",
                    Surface, row.MatchId, account.Puuid);
                continue;
            }

            keys.Add(new MatchSummaryKey(row.MatchId, row.ParticipantId.Value));
        }

        var hydrated = await hydrator.HydrateAsync(keys, ct);

        var matches = keys
            .Select(key => hydrated.TryGetValue(key, out var summary) ? summary : null)
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .ToList();

        return new MatchSummariesResponse
        {
            Matches = matches,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = total,
        };
    }
}
