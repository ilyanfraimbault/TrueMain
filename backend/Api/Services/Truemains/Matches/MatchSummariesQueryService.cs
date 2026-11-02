using Core.Lol.Map;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Matches;

public interface IMatchSummariesQueryService
{
    /// <summary>
    /// Returns the page of matches for the player identified by
    /// <paramref name="nameTag"/> (<c>gameName-tagLine</c>), ordered by game
    /// start time descending. Page numbers are 1-indexed; values outside the
    /// valid range are clamped. Optional <paramref name="position"/> and
    /// <paramref name="championId"/> filters narrow the feed to matches
    /// where the player played that lane / champion. Returns <c>null</c>
    /// when the name tag is malformed or no Riot account matches.
    /// </summary>
    Task<MatchSummariesResponse?> GetAsync(
        string nameTag,
        int page,
        int pageSize,
        string? position,
        int? championId,
        CancellationToken ct);
}

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
    TruemainAccountResolver resolver,
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
        // Normalize the position filter once. The DB stores team positions
        // as upper-case Riot strings (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY);
        // any other value clamps to null so a bogus query param doesn't
        // wedge the comparison.
        var normalizedPosition = LanePositions.Normalize(position);

        var championFilter = championId is > 0 ? championId : null;

        var account = await resolver.ResolveAsync(nameTag, ct);
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

        var liveTotal = await matchesQuery.CountAsync(ct);

        var truncatedSeats = TruncatedSeatsOf(account.Id, championFilter, normalizedPosition);
        var total = liveTotal + await truncatedSeats.CountAsync(ct);
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
        //
        // Offset paging on purpose (#1635): the depth is one player's history,
        // the count above already reads every row the slice could skip, and no
        // index yields a player's matches pre-sorted by start time, so a seek
        // would save nothing but the top-N sort. The id tie-break makes the
        // ordering unique, so two matches sharing a start time can never repeat
        // or vanish between pages.
        var skip = (clampedPage - 1) * clampedPageSize;
        var pageKeys = await matchesQuery
            .OrderByDescending(m => m.GameStartTimeUtc)
            .ThenByDescending(m => m.Id)
            .Skip(skip)
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

        // The page's rest, past the live games. A page past the last one comes back empty with
        // the real total, so the frontend's pagination control still resolves to a valid range.
        matches.AddRange(await ReadTruncatedPageAsync(
            truncatedSeats, Math.Max(0, skip - liveTotal), clampedPageSize - pageKeys.Count, ct));

        return new MatchSummariesResponse
        {
            Matches = matches,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = total,
        };
    }

    /// <summary>
    /// A player's games past match retention (#2358), which follow the live ones: retention only
    /// truncates what left the window. Keyed on the account, the only key a truncated seat keeps.
    /// </summary>
    private IQueryable<TruncatedMatchSeat> TruncatedSeatsOf(Guid accountId, int? championId, string? position)
        => db.TruncatedMatchSeats
            .AsNoTracking()
            .Where(seat => seat.RiotAccountId == accountId
                && (championId == null || seat.ChampionId == championId)
                && (position == null || seat.TeamPosition == position));

    private async Task<IReadOnlyList<MatchSummaryReadModel>> ReadTruncatedPageAsync(
        IQueryable<TruncatedMatchSeat> seats,
        int skip,
        int take,
        CancellationToken ct)
    {
        if (take <= 0)
        {
            return [];
        }

        var page = await seats
            .OrderByDescending(seat => seat.GameStartTimeUtc)
            .ThenByDescending(seat => seat.MatchId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return await TruncatedMatchSummaries.ReadAsync(db, page, ct);
    }
}
