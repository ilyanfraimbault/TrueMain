using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Stats;

public interface IChampionStatsQueryService
{
    Task<IReadOnlyList<ChampionStatRow>> GetAsync(
        string? region,
        string? patch,
        string? position,
        int? queue,
        CancellationToken ct);
}

public sealed class ChampionStatsQueryService(TrueMainDbContext db, IMemoryCache cache) : IChampionStatsQueryService
{
    // Plain 10-minute TTL rather than IChampionReadCache's aggregation-version
    // token (#1412). That token only advances when the pattern aggregation
    // publishes new champion_aggregate_scopes rows, but neither half of this read
    // comes from there: the "games" side reads champion_game_counts, which
    // ChampionGameCountRefreshProcess recomputes on its own cadence (#2132), and
    // the "mains" side (main_champion_stats) moves with the main analysis. A flat
    // TTL is the honest bound for both halves of this read.
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(10);

    // How long a failed computation is not re-run (#2132). The miss path is now a
    // small rollup read, so a failure is an outage rather than a slow scan — but the
    // operator pages still re-ask on every load, and a minute is long enough not to
    // pile a retry onto every one of them while short enough to recover on its own.
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromMinutes(1);

    // Single-flight for the miss path (#870): every concurrent caller that misses
    // together — the first request, or any request landing right after a TTL
    // expiry — joins one computation instead of each running it. Static for the same
    // reason as ChampionReadCache's coalescers: the point is to share across
    // concurrent *requests*, which each get their own scoped service instance.
    private static readonly RequestCoalescer<IReadOnlyList<ChampionStatRow>> ResponseCoalescer = new();

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

        return await ResponseCoalescer.GetOrJoinAsync(
            cacheKey,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a
                // pass that has since finished and cached its answer.
                if (cache.TryGetValue<IReadOnlyList<ChampionStatRow>>(cacheKey, out var justCached) && justCached is not null)
                {
                    return justCached;
                }

                var computed = await FailureBackoff.RunAsync(
                    cache,
                    cacheKey,
                    FailureBackoffWindow,
                    () => ComputeAsync(normalizedRegion, normalizedPatch, normalizedPosition, queue, CancellationToken.None));
                return cache.Store(cacheKey, computed, ResponseCacheTtl);
            },
            ct,
            // This factory reads through the request-scoped db above, so the shared
            // pass must not outlive the request that started it — same reasoning as
            // ChampionReadCache.GetOrComputeAsync.
            ownerAwaitsToCompletion: true);
    }

    private async Task<IReadOnlyList<ChampionStatRow>> ComputeAsync(
        string? normalizedRegion,
        string? normalizedPatch,
        string? normalizedPosition,
        int? queue,
        CancellationToken ct)
    {
        // Two independent per-champion aggregations folded together with a FULL
        // OUTER JOIN so a champion that appears in only one source (e.g. has
        // games but no mains yet, or vice-versa) still yields a row. The games
        // side honours region/patch/position/queue; the mains side honours region
        // only — patch, position and queue have no meaning for main_champion_stats,
        // which is computed per account-champion rather than per match.
        //
        // The games side reads champion_game_counts (#2132), never
        // match_participants: grouping the whole participant table on request hit
        // the API's 60 s statement_timeout on a cold cache. Every filter of this read
        // is a dimension of that rollup, so any combination is a SUM over a few
        // thousand rows. The rollup is the raw rows' count as of its last refresh
        // (each slice is recomputed, not folded once), so it follows retention and
        // the lane fix the way the live count did, a refresh behind.
        //
        // champion_aggregate_scopes is not a substitute: it only covers tracked
        // accounts' own games (the main_champion_stats population, optionally widened
        // to non-mains), never the untracked teammates/opponents sharing the same
        // matches, so reading "Games" from it would silently undercount against this
        // read's population. Patch matches the rollup's copy of matches."Patch" —
        // the normalised "MAJOR.MINOR" form — and an unparseable version is stored
        // as '', which no trimmed, non-empty patch filter can ask for.
        // Each nullable filter is guarded by an "{param}::type IS NULL OR ..."
        // clause so a null parameter means "no filter".
        FormattableString sql = $"""
            WITH games AS (
                SELECT c."ChampionId" AS "ChampionId", SUM(c."Games") AS "Games"
                FROM champion_game_counts c
                WHERE ({normalizedRegion}::text IS NULL OR c."PlatformId" = {normalizedRegion})
                  AND ({queue}::int IS NULL OR c."QueueId" = {queue})
                  AND ({normalizedPosition}::text IS NULL OR c."TeamPosition" = {normalizedPosition})
                  AND ({normalizedPatch}::text IS NULL OR c."Patch" = {normalizedPatch})
                GROUP BY c."ChampionId"
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

        return rows
            .Select(row => new ChampionStatRow
            {
                ChampionId = row.ChampionId,
                Games = row.Games,
                Mains = row.Mains,
                Otps = row.Otps,
                ExtendedSamples = row.ExtendedSamples
            })
            .ToList();
    }

    /// <summary>
    /// Cache key for the (region, patch, position, queue) filter tuple. Every
    /// slot is rendered explicitly — including the "unset" placeholder — so two
    /// distinct filter combinations, unset or not, can never collide on slot
    /// <em>count</em>. That alone is not enough: joining raw values with an
    /// unescaped <c>:</c> lets the delimiter itself be forged — region "A:B" +
    /// patch "C" and region "A" + patch "B:C" both render "A:B:C". Each string
    /// slot is therefore backslash-escaped before joining, which makes the
    /// delimiter unambiguous regardless of what a caller passes as a filter.
    /// </summary>
    internal static string BuildCacheKey(string? region, string? patch, string? position, int? queue)
        => $"ops:champion-stats:{EscapeKeySegment(region)}:{EscapeKeySegment(patch)}:{EscapeKeySegment(position)}:{queue?.ToString() ?? "_"}";

    /// <summary>
    /// Escapes <c>\</c> and <c>:</c> so the segment can be joined with <c>:</c>-delimited
    /// neighbours without the delimiter being spoofable from inside a filter value. The
    /// placeholder for "unset" (<c>_</c>) is applied after escaping — it contains neither
    /// character, so the order does not matter, but escaping happens on every path.
    /// </summary>
    private static string EscapeKeySegment(string? value)
        => value is null ? "_" : value.Replace("\\", "\\\\").Replace(":", "\\:");

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ChampionStatRowResult(
        int ChampionId,
        long Games,
        int Mains,
        int Otps,
        int ExtendedSamples);
}
