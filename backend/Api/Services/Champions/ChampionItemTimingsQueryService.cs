using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Average first-purchase time of each item for a champion at a position — the
/// "power spike" timeline. Unnests the participants' ITEM_PURCHASED events
/// (stored as jsonb on match_participants), takes the first purchase of each item
/// per game, and averages across games above the sample floor. Same queue / patch
/// / tracked-account population as the sibling champion reads, cached 60s.
/// </summary>
public sealed class ChampionItemTimingsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionItemTimingsQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<ChampionItemTimingsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause). A null
        // array parameter short-circuits the WHERE via the `IS NULL OR` guard,
        // mirroring the patch-prefix pattern below. The cache key carries the band.
        var bands = EloBracket.ResolveFilter(eloBracket);
        var bandsArray = bands?.ToArray();
        var bracketToken = EloBracket.ResolveToken(eloBracket);

        var cacheKey = $"champions:item-timings:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";
        if (cache.TryGetValue<ChampionItemTimingsResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";
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
              AND ({patchPrefix}::text IS NULL OR m.""GameVersion"" LIKE {patchPrefix})
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

        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return response;
    }

    private sealed record ItemTimingRow(int ItemId, int Games, double AvgSeconds);
}
