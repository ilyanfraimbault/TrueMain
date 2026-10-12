using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Builds;

public interface IChampionItemTimingsQueryService
{
    Task<ChampionItemTimingsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct);
}

/// <summary>
/// Average first-purchase time of each item for a champion at a position — the
/// item timeline. Unnests the participants' ITEM_PURCHASED events
/// (stored as jsonb on match_participants), takes the first purchase of each item
/// per game, and averages across games above the sample floor. Counts the champion's
/// cohort (<see cref="ChampionCohort"/>), the population of every other panel on the
/// page; cached and coalesced by aggregation version like all of them.
/// </summary>
public sealed class ChampionItemTimingsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IChampionReadCache cache)
    : IChampionItemTimingsQueryService
{
    public Task<ChampionItemTimingsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = PatchFilter.Normalize(patch);

        // The key carries the band; the aggregation version is stamped on by the cache.
        var bracketToken = EloBracket.ResolveToken(eloBracket);
        var cacheKey = $"champions:item-timings:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";

        return cache.GetOrComputeAsync(
            cacheKey,
            token => ComputeAsync(championId, position, normalizedPatch, eloBracket, token),
            ct);
    }

    private async Task<ChampionItemTimingsResponse> ComputeAsync(
        int championId,
        string position,
        string? normalizedPatch,
        string? eloBracket,
        CancellationToken ct)
    {
        // Resolve the elo filter to its bands (null = ALL, no clause).
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);

        var queueId = (int)options.Value.QueueId;
        var minGames = championsOptions.Value.MinMatchupGames;

        var rows = await TimingsQuery(db, championId, position, queueId, normalizedPatch, bands, minGames)
            .ToListAsync(ct);

        var items = rows
            .Select(row => new ChampionItemTiming
            {
                ItemId = row.ItemId,
                Games = row.Games,
                AvgSeconds = row.AvgMs / 1000.0
            })
            .ToList();

        var response = new ChampionItemTimingsResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Items = items
        };

        return response;
    }

    /// <summary>
    /// The whole read as one query, split out so its SQL shape can be pinned without a
    /// database: the cohort composed in LINQ, the purchase unnest in raw SQL beneath it.
    /// </summary>
    internal static IQueryable<ItemTimingRow> TimingsQuery(
        TrueMainDbContext db,
        int championId,
        string position,
        int queueId,
        string? normalizedPatch,
        IReadOnlyCollection<string>? bands,
        int minGames)
    {
        // Who counts: the champion's cohort on this lane, queue, patch and bands —
        // composed from ChampionCohort, never restated in the SQL below.
        var members = ChampionCohort.Members(db, queueId, normalizedPatch)
            .Where(p => p.ChampionId == championId && p.TeamPosition == position);
        if (bands is not null)
        {
            members = members.Where(p => bands.Contains(p.EloBracket));
        }

        // What each of them bought: per participant row of this champion and lane, the
        // first purchase time of each item (MIN over its purchases). The CROSS JOIN
        // LATERAL unnests the ITEM_PURCHASED events from the jsonb column, which LINQ
        // cannot reach; the champion and lane are repeated here only so the scan seeks
        // the (champion, lane) index before the cohort narrows it. Interpolated values
        // are parameterised by SqlQuery, so the jsonb literals are the only inline SQL.
        FormattableString sql = $@"
            SELECT mp.""Id"" AS ""ParticipantRowId"",
                   e.item_id AS ""ItemId"",
                   e.ts AS ""TimestampMs""
            FROM match_participants mp
            CROSS JOIN LATERAL (
                SELECT (ev->>'ItemId')::int AS item_id,
                       MIN((ev->>'TimestampMs')::int) AS ts
                FROM jsonb_array_elements(mp.""ItemEvents"") ev
                WHERE ev->>'EventType' = 'ITEM_PURCHASED' AND (ev->>'ItemId')::int > 0
                GROUP BY (ev->>'ItemId')::int
            ) e
            WHERE mp.""ChampionId"" = {championId}
              AND mp.""TeamPosition"" = {position}";

        // The average of those first purchases across the cohort's games, floored and
        // ordered in SQL — one round trip, the raw query composed as a subquery.
        return db.Database.SqlQuery<FirstPurchaseRow>(sql)
            .Where(purchase => members.Any(p => p.Id == purchase.ParticipantRowId))
            .GroupBy(purchase => purchase.ItemId)
            .Select(g => new ItemTimingRow
            {
                ItemId = g.Key,
                Games = g.Count(),
                AvgMs = g.Average(purchase => (double)purchase.TimestampMs),
            })
            .Where(row => row.Games >= minGames)
            .OrderBy(row => row.AvgMs);
    }

    /// <summary>One participant row's first purchase of one item, from the raw unnest.</summary>
    internal sealed record FirstPurchaseRow(Guid ParticipantRowId, int ItemId, int TimestampMs);

    /// <summary>One item's average first-purchase time across the cohort's games.</summary>
    /// <remarks>Object-initializer form: Npgsql translates a later filter over it cleanly.</remarks>
    internal sealed record ItemTimingRow
    {
        public required int ItemId { get; init; }

        public required int Games { get; init; }

        public required double AvgMs { get; init; }
    }
}
