using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ChampionStatsQueryService(TrueMainDbContext db, IMemoryCache cache) : IChampionStatsQueryService
{
    // Plain 10-minute TTL rather than IChampionReadCache's aggregation-version
    // token (#1412). That token only advances when the pattern aggregation
    // publishes new champion_aggregate_scopes rows (every ~1-2h), but this
    // read's "games" side scans match_participants/matches live — the same
    // tables ordinary ingestion keeps writing between aggregation cycles — so
    // the token would leave this endpoint serving stale numbers for up to that
    // whole window instead of the ten minutes an operator page actually needs.
    // The "mains" side (main_champion_stats) moves on its own analysis cadence
    // too, unrelated to the aggregation stamp. A flat TTL is the honest bound
    // for both halves of this read.
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<ChampionStatRow>> GetAsync(
        string? region,
        string? patch,
        string? position,
        int? queue,
        CancellationToken ct)
    {
        var normalizedRegion = Trimmed(region);
        var normalizedPatch = Trimmed(patch);
        var normalizedPosition = Trimmed(position);

        // Cache key is the full filter tuple: a filtered call must never read
        // (or overwrite) the unfiltered entry, so every one of the four
        // independent filters — including the ones left unset — has to appear
        // in the key (see CacheKeyDisciplineTests).
        var cacheKey = BuildCacheKey(normalizedRegion, normalizedPatch, normalizedPosition, queue);
        if (cache.TryGetValue<IReadOnlyList<ChampionStatRow>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        // Two independent per-champion aggregations folded together with a FULL
        // OUTER JOIN so a champion that appears in only one source (e.g. has
        // games but no mains yet, or vice-versa) still yields a row. The games
        // side honours region/patch/position/queue (joining matches for the
        // match-scoped filters); the mains side honours region only — patch,
        // position and queue have no meaning for main_champion_stats, which is
        // computed per account-champion rather than per match.
        //
        // Patch is matched against the stored generated column matches."Patch"
        // (#1368) rather than recomputed per row via split_part(GameVersion, ...):
        // the column already carries the normalised "MAJOR.MINOR" form and is
        // covered by IX_matches_patch_queue, so this filter is an index lookup
        // instead of a function call evaluated over every scanned row.
        // Each nullable filter is guarded by an "{param}::type IS NULL OR ..."
        // clause so a null parameter means "no filter".
        //
        // The unfiltered "games" CTE still scans every match_participants row
        // for the (region, queue, position) combination — champion_aggregate_scopes
        // is not a substitute here: it only covers tracked accounts' own games
        // (the main_champion_stats population, optionally widened to
        // non-mains), never the untracked teammates/opponents sharing the same
        // matches, so reading "Games" from it would silently undercount against
        // this read's current population. Caching is what makes the unfiltered
        // (Overview top-10) call cheap instead of touching the aggregate tables.
        FormattableString sql = $"""
            WITH games AS (
                SELECT p."ChampionId" AS "ChampionId", COUNT(*) AS "Games"
                FROM match_participants p
                INNER JOIN matches m ON m."Id" = p."MatchId"
                WHERE ({normalizedRegion}::text IS NULL OR m."PlatformId" = {normalizedRegion})
                  AND ({queue}::int IS NULL OR m."QueueId" = {queue})
                  AND ({normalizedPosition}::text IS NULL OR p."TeamPosition" = {normalizedPosition})
                  AND ({normalizedPatch}::text IS NULL OR m."Patch" = {normalizedPatch})
                GROUP BY p."ChampionId"
            ),
            mains AS (
                SELECT
                    s."ChampionId" AS "ChampionId",
                    -- Active mains only (#900): this column is the operator's view of the same
                    -- pool the coverage signal scores on, so a retired main must not make a
                    -- champion look covered when scarcity still treats it as under-covered.
                    COUNT(*) FILTER (WHERE s."IsMain" AND s."IsActive") AS "Mains",
                    COUNT(*) FILTER (WHERE s."IsOtp") AS "Otps",
                    COUNT(*) FILTER (WHERE s."IsExtendedSample") AS "ExtendedSamples"
                FROM main_champion_stats s
                WHERE ({normalizedRegion}::text IS NULL OR s."PlatformId" = {normalizedRegion})
                GROUP BY s."ChampionId"
            )
            SELECT
                COALESCE(g."ChampionId", mn."ChampionId") AS "ChampionId",
                COALESCE(g."Games", 0)::bigint AS "Games",
                COALESCE(mn."Mains", 0)::int AS "Mains",
                COALESCE(mn."Otps", 0)::int AS "Otps",
                COALESCE(mn."ExtendedSamples", 0)::int AS "ExtendedSamples"
            FROM games g
            FULL OUTER JOIN mains mn ON mn."ChampionId" = g."ChampionId"
            ORDER BY "Games" DESC, "ChampionId"
            """;

        var rows = await db.Database.SqlQuery<ChampionStatRowResult>(sql).ToListAsync(ct);

        var result = rows
            .Select(row => new ChampionStatRow
            {
                ChampionId = row.ChampionId,
                Games = row.Games,
                Mains = row.Mains,
                Otps = row.Otps,
                ExtendedSamples = row.ExtendedSamples
            })
            .ToList();

        return cache.Store(cacheKey, (IReadOnlyList<ChampionStatRow>)result, ResponseCacheTtl);
    }

    /// <summary>
    /// Cache key for the (region, patch, position, queue) filter tuple. Every
    /// slot is rendered explicitly — including the "unset" placeholder — so two
    /// distinct filter combinations, unset or not, can never collide.
    /// </summary>
    internal static string BuildCacheKey(string? region, string? patch, string? position, int? queue)
        => $"ops:champion-stats:{region ?? "_"}:{patch ?? "_"}:{position ?? "_"}:{queue?.ToString() ?? "_"}";

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ChampionStatRowResult(
        int ChampionId,
        long Games,
        int Mains,
        int Otps,
        int ExtendedSamples);
}
