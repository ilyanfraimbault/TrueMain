using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Average first-purchase time of each item for a champion at a position — the
/// "power spike" timeline. Unnests the participants' ITEM_PURCHASED events
/// (stored as jsonb on match_participants), takes the first purchase of each item
/// per game, and averages across games above the sample floor. Same queue / patch
/// / tracked-account population as the sibling champion reads; cached and
/// coalesced by aggregation version like all of them.
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
        // Resolve the elo filter to its bands (null = ALL, no clause). A null array
        // parameter short-circuits the WHERE via the `IS NULL OR` guard, the same way
        // the patch parameter below does.
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var bandsArray = bands?.ToArray();

        var queueId = (int)options.Value.QueueId;
        var minGames = championsOptions.Value.MinMatchupGames;

        // Per game, the first purchase time of each item (MIN over its purchases),
        // then the average of those times across games. The CROSS JOIN LATERAL
        // unnests the ITEM_PURCHASED events from the jsonb column. All interpolated
        // values are parameterised by EF Core's SqlQuery, so the jsonb literals
        // ('ITEM_PURCHASED', key names) are the only inline SQL — no user input.
        FormattableString sql = $@"
            SELECT e.item_id AS ""ItemId"",
                   COUNT(*)::int AS ""Games"",
                   (AVG(e.ts) / 1000.0)::double precision AS ""AvgSeconds""
            FROM match_participants mp
            JOIN matches m ON m.""Id"" = mp.""MatchId""
            CROSS JOIN LATERAL (
                SELECT (ev->>'ItemId')::int AS item_id,
                       MIN((ev->>'TimestampMs')::int) AS ts
                FROM jsonb_array_elements(mp.""ItemEvents"") ev
                WHERE ev->>'EventType' = 'ITEM_PURCHASED' AND (ev->>'ItemId')::int > 0
                GROUP BY (ev->>'ItemId')::int
            ) e
            WHERE mp.""ChampionId"" = {championId}
              AND mp.""TeamPosition"" = {position}
              AND mp.""RiotAccountId"" IS NOT NULL
              AND m.""QueueId"" = {queueId}
              AND ({normalizedPatch}::text IS NULL OR m.""Patch"" = {normalizedPatch})
              AND ({bandsArray}::text[] IS NULL OR mp.""elo_bracket"" = ANY({bandsArray}::text[]))
            GROUP BY e.item_id
            HAVING COUNT(*) >= {minGames}
            ORDER BY AVG(e.ts)";

        var rows = await db.Database.SqlQuery<ItemTimingRow>(sql).ToListAsync(ct);

        var items = rows
            .Select(row => new ChampionItemTiming
            {
                ItemId = row.ItemId,
                Games = row.Games,
                AvgSeconds = row.AvgSeconds
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

    private sealed record ItemTimingRow(int ItemId, int Games, double AvgSeconds);
}
