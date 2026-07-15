using System.Diagnostics;
using Core.Lol.Map;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public sealed class TruemainsLeaderboardQueryService(
    TrueMainDbContext db,
    IDbContextFactory<TrueMainDbContext> dbFactory,
    IOptions<TruemainsLeaderboardOptions> options,
    IMemoryCache cache,
    ILogger<TruemainsLeaderboardQueryService> logger) : ITruemainsLeaderboardQueryService
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 50;
    private const int TopChampionsPerRow = 3;

    // The leaderboard is dominated by page-1 traffic with no filters, and the
    // four SQL queries that make a fresh response (Count + FetchPage +
    // FetchTopChampions + FetchStats) cost more than the response itself is
    // worth re-deriving every second. The numbers underneath only shift when
    // the ingestor flushes a new batch of rank snapshots or matches — that
    // happens on a multi-minute cadence, so a 30s TTL trades a few seconds of
    // staleness for a massive drop in DB load. Mirrors the TTL
    // ChampionSummariesQueryService uses for the same reason.
    private static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromSeconds(30);

    // Ranked solo queue. Matches the queue used by MainStatsCalculator
    // for main_champion_stats, so the "games" / KDA / winrate cell stays
    // consistent with the player's top-champions cell on the same row.
    private const int RankedQueueId = (int)LolQueueId.RankedSoloDuo;

    // A position filter surfaces every player who plays that position on a
    // main champion at least this share of the time, not just players whose
    // single most-played position matches. The bar is low (20%) on purpose:
    // any meaningful flex into the lane should make the player visible there,
    // and only one-off cameos (<20% of a champion's games) stay filtered out.
    // Shared with the primary/secondary derivation in MainPositions so the
    // filter bar and the "secondary lane" bar can't drift apart.
    private const double MinPositionShare = MainPositions.MinShare;

    public async Task<LeaderboardResponse> GetAsync(
        int page,
        int pageSize,
        string? region,
        string? position,
        int? championId,
        CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();

        var normalizedPosition = NormalizePosition(position);
        var championFilter = championId is > 0 ? championId : null;
        var clampedPageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var clampedPage = Math.Max(page, 1);
        var offset = (clampedPage - 1) * clampedPageSize;

        // Region narrowing: an explicit ?region= picks one of the three
        // exposed pills, anything else falls back to "every platform the
        // leaderboard surfaces" so the count and the page slice agree on
        // which accounts are eligible.
        var platforms = (RegionFilterParser.Parse(region)
                         ?? RegionFilterParser.AllExposedPlatforms())
                        .ToArray();

        if (platforms.Length == 0)
        {
            return Empty(clampedPage, clampedPageSize);
        }

        var minGames = Math.Max(0, options.Value.MinRankedGames);

        // Page-1-no-filter is the dominant shape of /truemains traffic; the
        // four SQL queries that compose a fresh response cost far more than
        // the JSON is worth re-deriving every second. Cache the assembled
        // response keyed on the request shape — the snapshot rate underneath
        // moves on a multi-minute cadence (RankSnapshotIngestion + match
        // ingest), so the TTL trades a few seconds of staleness for a large
        // drop in DB load. Mirrors ChampionSummariesQueryService's TTL.
        var cacheKey = BuildCacheKey(platforms, championFilter, normalizedPosition, minGames, clampedPage, clampedPageSize);
        if (cache.TryGetValue<LeaderboardResponse>(cacheKey, out var cached) && cached is not null)
        {
            totalSw.Stop();
            logger.LogInformation(
                "[truemain-leaderboard] page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} rows={Rows} total={Total} elapsed={ElapsedMs}ms result=cache_hit",
                clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames,
                cached.Rows.Count, cached.Total, totalSw.ElapsedMilliseconds);
            return cached;
        }

        var (total, countMs) = await TimedAsync(() =>
            CountAsync(platforms, championFilter, normalizedPosition, minGames, ct));
        if (total == 0)
        {
            var empty = Empty(clampedPage, clampedPageSize);
            // Cache the empty response — a filter that yields nothing still
            // pays for the Count SQL on every visit, and those are the same
            // requests an attacker / overzealous client would replay.
            cache.Set(cacheKey, empty, CacheEntry(ResponseCacheTtl));
            // An empty result is exactly when countMs is worth seeing: an
            // over-restrictive filter or a misconfigured MinRankedGames is
            // diagnosed here, not on the populated path.
            totalSw.Stop();
            logger.LogInformation(
                "[truemain-leaderboard] page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} rows=0 total=0 countMs={CountMs:F1} elapsed={ElapsedMs}ms result=empty",
                clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames,
                countMs, totalSw.ElapsedMilliseconds);
            return empty;
        }

        var (pageRows, pageMs) = await TimedAsync(() => FetchPageAsync(
            platforms, championFilter, normalizedPosition, minGames, offset, clampedPageSize, ct));
        if (pageRows.Count == 0)
        {
            // The caller asked for a page past the end. Return an empty slice
            // with the real total so the frontend's pagination control still
            // resolves to a valid range without a second round trip.
            var pastEnd = new LeaderboardResponse
            {
                Rows = Array.Empty<LeaderboardRowReadModel>(),
                Page = clampedPage,
                PageSize = clampedPageSize,
                Total = total,
            };
            cache.Set(cacheKey, pastEnd, CacheEntry(ResponseCacheTtl));
            totalSw.Stop();
            logger.LogInformation(
                "[truemain-leaderboard] page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} rows=0 total={Total} countMs={CountMs:F1} pageMs={PageMs:F1} elapsed={ElapsedMs}ms result=past_end",
                clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames,
                total, countMs, pageMs, totalSw.ElapsedMilliseconds);
            return pastEnd;
        }

        // Hydrate the page slice with derived data. Four batched queries — the
        // top-3 champions (main_champion_stats) and KDA / W-L (match_participants)
        // keyed by puuid, the latest rank cells (tier/div/LP) keyed by account id,
        // and the dominant build per (account, champion) from the aggregate
        // schema. The heavy ordering + pagination already happened on
        // riot_accounts."Score", so these only touch the ~25 rows on the page.
        //
        // stats / ranks depend only on the page's puuid / id arrays, so they
        // fire immediately. The build fetch needs the actual champion ids the
        // top-3 query selects, so it chains off topChampions — but that chained
        // pair still overlaps stats / ranks, so the wall-clock hydration cost is
        // unchanged. A single DbContext is not thread-safe, so each Fetch creates
        // its own short-lived context from the factory (mirrors
        // ProfileQueryService). Each task still times its own body via TimedAsync
        // so the cache-miss log keeps the per-phase breakdown; under concurrency
        // those spans overlap, so they sum to more than the wall-clock hydration
        // time — that's expected and the point (it shows which round trip
        // dominates, not how long the phase took).
        var puuids = pageRows.Select(r => r.Puuid).ToArray();
        var accountIds = pageRows.Select(r => r.Id).ToArray();
        var accountIdByPuuid = pageRows
            .GroupBy(r => r.Puuid)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var statsTask = TimedAsync(() => FetchStatsAsync(puuids, ct));
        var ranksTask = TimedAsync(() => FetchLatestRanksAsync(accountIds, ct));
        var positionsTask = TimedAsync(() => FetchPositionsAsync(puuids, ct));

        // Chain the build fetch off the top-3 result: it resolves the page's
        // (account, champion) pairs from the selected champions, and still runs
        // alongside stats / ranks. The metric is named `buildsContinuationMs`
        // because TimedAsync wraps the whole continuation — the wait on
        // topChampionsTask *plus* the build round trips — so it is an upper
        // bound, not the build query in isolation.
        var topChampionsTask = TimedAsync(() => FetchTopChampionsAsync(puuids, ct));
        var buildsTask = TimedAsync(async () =>
        {
            var (topChampions, _) = await topChampionsTask;
            return await FetchTopChampionBuildsAsync(topChampions, accountIdByPuuid, ct);
        });

        await Task.WhenAll(topChampionsTask, statsTask, ranksTask, buildsTask, positionsTask);

        var (topChampionsByPuuid, topChampMs) = await topChampionsTask;
        var (statsByPuuid, statsMs) = await statsTask;
        var (ranksByAccount, ranksMs) = await ranksTask;
        var (buildsByPuuidChampion, buildsContinuationMs) = await buildsTask;
        var (positionsByPuuid, positionsMs) = await positionsTask;

        var rank = offset + 1;
        var rows = new List<LeaderboardRowReadModel>(pageRows.Count);
        foreach (var row in pageRows)
        {
            var topChamps = topChampionsByPuuid.GetValueOrDefault(row.Puuid)
                            ?? new List<LeaderboardTopChampionReadModel>();
            // Enrich each top champion with the player's dominant build. The
            // build is absent for champions the aggregate pipeline hasn't
            // produced a pattern for yet — the three ids stay null then, never
            // throwing (GetValueOrDefault returns the struct's default).
            topChamps = topChamps
                .Select(champion =>
                {
                    var build = buildsByPuuidChampion.GetValueOrDefault((row.Puuid, champion.ChampionId));
                    return champion with
                    {
                        PrimaryKeystoneId = build.PrimaryKeystoneId,
                        SecondaryStyleId = build.SecondaryStyleId,
                        FirstItemId = build.FirstItemId,
                    };
                })
                .ToList();
            var stats = statsByPuuid.GetValueOrDefault(row.Puuid);
            var latestRank = ranksByAccount.GetValueOrDefault(row.Id);

            // platformId → region slug. RouteToSlug can return null for
            // platforms we don't expose (JP1/SEA); the platforms filter
            // already excluded them, so this is just a defensive default.
            var regionSlug = RegionFilterParser.RouteToSlug(row.PlatformId) ?? string.Empty;

            rows.Add(new LeaderboardRowReadModel
            {
                Rank = rank++,
                Identity = new ProfileIdentityReadModel
                {
                    GameName = row.GameName,
                    TagLine = row.TagLine,
                    PlatformId = row.PlatformId,
                    ProfileIconId = row.ProfileIconId,
                    SummonerLevel = row.SummonerLevel,
                },
                Region = regionSlug,
                Ranked = new LeaderboardRankedReadModel
                {
                    Tier = latestRank?.Tier ?? string.Empty,
                    Division = latestRank?.Division ?? string.Empty,
                    LeaguePoints = latestRank?.LeaguePoints ?? 0,
                    Score = row.Score,
                },
                Stats = new LeaderboardStatsReadModel
                {
                    Games = stats?.Games ?? 0,
                    Wins = stats?.Wins,
                    Losses = stats?.Losses,
                    WinRate = stats is not null
                        ? RateMath.WinRate(stats.Wins, stats.Losses)
                        : null,
                    Kda = stats?.Kda,
                },
                TopChampions = topChamps,
                Positions = positionsByPuuid.GetValueOrDefault(row.Puuid),
            });
        }

        var response = new LeaderboardResponse
        {
            Rows = rows,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = total,
        };
        cache.Set(cacheKey, response, CacheEntry(ResponseCacheTtl));

        totalSw.Stop();
        logger.LogInformation(
            "[truemain-leaderboard] page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} rows={Rows} total={Total} countMs={CountMs:F1} pageMs={PageMs:F1} topChampMs={TopChampMs:F1} statsMs={StatsMs:F1} ranksMs={RanksMs:F1} buildsContinuationMs={BuildsContinuationMs:F1} positionsMs={PositionsMs:F1} elapsed={ElapsedMs}ms result=miss",
            clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames,
            rows.Count, total, countMs, pageMs, topChampMs, statsMs, ranksMs, buildsContinuationMs, positionsMs, totalSw.ElapsedMilliseconds);

        return response;
    }

    private static string BuildCacheKey(
        string[] platforms,
        int? championFilter,
        string? position,
        int minGames,
        int page,
        int pageSize)
    {
        // Caller-stable: platforms are normalised upstream (RegionFilterParser
        // returns a deterministic iteration), but sorting defends against
        // future drift if another caller passes them in a different order.
        // The "_" sentinel keeps nullable values distinct from any literal
        // filter value that could collide on the key (no champion uses "_"
        // as an ID, no position is "_").
        var platformPart = string.Join(",", platforms.OrderBy(p => p, StringComparer.Ordinal));
        var championPart = championFilter?.ToString() ?? "_";
        var positionPart = position ?? "_";
        return $"truemains:leaderboard:{platformPart}:{championPart}:{positionPart}:{minGames}:{page}:{pageSize}";
    }

    // Every cache entry must carry a Size because the shared MemoryCache runs
    // with a SizeLimit (see Program.cs). Without a Size the Set is silently
    // dropped and the value never caches — a cache-miss storm, not an error.
    // Count-based: one entry counts as one unit.
    private static MemoryCacheEntryOptions CacheEntry(TimeSpan ttl) => new()
    {
        AbsoluteExpirationRelativeToNow = ttl,
        Size = 1,
    };

    // Times a single sub-query so the cache-miss log line can carry a per-phase
    // latency breakdown (countMs/pageMs/topChampMs/statsMs/ranksMs). The whole
    // point of #195 is knowing which of the independent SQL round trips
    // dominates on prod-shaped data, so the breakdown is part of the structured
    // log, not throwaway debug. TotalMilliseconds (fractional) because a warm
    // index scan over the page's ~25 rows can finish well under 1ms.
    private static async Task<(T Result, double ElapsedMs)> TimedAsync<T>(Func<Task<T>> query)
    {
        var sw = Stopwatch.StartNew();
        var result = await query();
        sw.Stop();
        return (result, sw.Elapsed.TotalMilliseconds);
    }

    private async Task<int> CountAsync(
        string[] platforms,
        int? championFilter,
        string? position,
        int minGames,
        CancellationToken ct)
    {
        // The /truemains leaderboard is, by definition, the list of truemains
        // — so the `IsMain = true` EXISTS is unconditional. Accounts that
        // haven't been through main analysis yet (fresh ingests) are out of
        // scope until they do. The `championFilter` / `position` parameters
        // degrade to "any champion / any position" via IS NULL so they
        // compose inside the same EXISTS clause without an outer toggle.
        //
        // The ranked-games floor reads main_champion_stats."TotalMatches"
        // rather than a correlated COUNT(*) over match_participants: that
        // subquery ran once per candidate account and dominated the whole
        // query, yet filtered nothing — main analysis only sets IsMain when
        // TotalMatches >= MinMatchesToEvaluate (20), so every row the EXISTS
        // already admits clears the same bar. TotalMatches saturates at
        // MainAnalysis.MatchesToConsider (50), so minGames must stay <= 50 to
        // remain meaningful.
        FormattableString sql = $"""
            SELECT COUNT(*)::int AS "Value"
            FROM riot_accounts a
            WHERE a."PlatformId" = ANY ({platforms})
              AND a."Score" IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM main_champion_stats m
                  WHERE m."PlatformId" = a."PlatformId"
                    AND m."Puuid" = a."Puuid"
                    AND m."IsMain" = true
                    AND m."TotalMatches" >= {minGames}
                    AND ({championFilter}::int IS NULL OR m."ChampionId" = {championFilter})
                    AND ({position}::text IS NULL OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                        WHERE pos->>'Position' = {position}
                          AND (pos->>'Rate')::float8 >= {MinPositionShare}
                    ))
              )
            """;

        // SqlQuery wraps this as a subquery; the COUNT always yields exactly one
        // row, so SingleAsync states that invariant — and avoids EF's spurious
        // "First/FirstOrDefault without OrderBy" warning (event 10103) that the
        // wrapper query otherwise triggers.
        return await db.Database.SqlQuery<int>(sql).SingleAsync(ct);
    }

    private async Task<List<PageRow>> FetchPageAsync(
        string[] platforms,
        int? championFilter,
        string? position,
        int minGames,
        int offset,
        int pageSize,
        CancellationToken ct)
    {
        // Order + paginate directly on the denormalised riot_accounts."Score"
        // (maintained by RankSnapshotWriter) — no DISTINCT ON, no inline score
        // CASE. "Score IS NOT NULL" is the is-ranked gate and must stay in
        // lock-step with CountAsync or pagination drifts from the total. The
        // tier/div/LP display cells are hydrated per page in GetAsync.
        FormattableString sql = $"""
            SELECT
                a."Id" AS "Id",
                a."Puuid" AS "Puuid",
                a."GameName" AS "GameName",
                a."TagLine" AS "TagLine",
                a."PlatformId" AS "PlatformId",
                a."ProfileIconId" AS "ProfileIconId",
                a."SummonerLevel" AS "SummonerLevel",
                a."Score" AS "Score"
            FROM riot_accounts a
            WHERE a."PlatformId" = ANY ({platforms})
              AND a."Score" IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM main_champion_stats m
                  WHERE m."PlatformId" = a."PlatformId"
                    AND m."Puuid" = a."Puuid"
                    AND m."IsMain" = true
                    AND m."TotalMatches" >= {minGames}
                    AND ({championFilter}::int IS NULL OR m."ChampionId" = {championFilter})
                    AND ({position}::text IS NULL OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                        WHERE pos->>'Position' = {position}
                          AND (pos->>'Rate')::float8 >= {MinPositionShare}
                    ))
              )
            ORDER BY a."Score" DESC, a."Id"
            LIMIT {pageSize} OFFSET {offset}
            """;

        return await db.Database.SqlQuery<PageRow>(sql).ToListAsync(ct);
    }

    private async Task<Dictionary<Guid, RankRow>> FetchLatestRanksAsync(Guid[] accountIds, CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return new Dictionary<Guid, RankRow>();
        }

        // Own short-lived context: this runs concurrently with FetchTopChampions
        // and FetchStats, and a single DbContext is not thread-safe.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        // Latest rank per page account for the display cells (tier/div/LP).
        // Only the page's ~25 accounts — the heavy ordering + pagination already
        // happened on riot_accounts."Score" — so this DISTINCT ON is cheap.
        FormattableString sql = $"""
            SELECT DISTINCT ON (rs."RiotAccountId")
                rs."RiotAccountId" AS "AccountId",
                rs."Tier" AS "Tier",
                rs."Division" AS "Division",
                rs."LeaguePoints" AS "LeaguePoints"
            FROM rank_snapshots rs
            WHERE rs."RiotAccountId" = ANY ({accountIds})
            ORDER BY rs."RiotAccountId", rs."CapturedAtUtc" DESC
            """;

        var rows = await ctx.Database.SqlQuery<RankRow>(sql).ToListAsync(ct);
        return rows.ToDictionary(r => r.AccountId);
    }

    private async Task<Dictionary<string, List<LeaderboardTopChampionReadModel>>> FetchTopChampionsAsync(
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, List<LeaderboardTopChampionReadModel>>();
        }

        // Own short-lived context: this runs concurrently with FetchStats and
        // FetchLatestRanks, and a single DbContext is not thread-safe.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var take = TopChampionsPerRow;
        // ROW_NUMBER per puuid keeps the top-3 cap inside the database so we
        // don't fetch every main row only to throw most away in C#. PlayRate
        // ties tend to coincide with championMatches ties, so the secondary
        // sort matches ProfileQueryService for consistency.
        FormattableString sql = $"""
            WITH ranked AS (
                SELECT
                    m."Puuid" AS "Puuid",
                    m."ChampionId" AS "ChampionId",
                    m."ChampionMatches" AS "Games",
                    m."PlayRate" AS "PlayRate",
                    ROW_NUMBER() OVER (
                        PARTITION BY m."Puuid"
                        ORDER BY m."PlayRate" DESC, m."ChampionMatches" DESC
                    ) AS rn
                FROM main_champion_stats m
                WHERE m."Puuid" = ANY ({puuids})
                  AND m."IsMain" = true
            )
            SELECT "Puuid", "ChampionId", "Games", "PlayRate"
            FROM ranked
            WHERE rn <= {take}
            ORDER BY "Puuid", rn
            """;

        var rows = await ctx.Database.SqlQuery<TopChampionRow>(sql).ToListAsync(ct);

        // PlayRate is stored 0..1 by main analysis (MainStatsCalculator computes
        // championMatches / totalMatches), matching the JSON contract — passed
        // through without rescaling.
        return rows
            .GroupBy(r => r.Puuid)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new LeaderboardTopChampionReadModel
                {
                    ChampionId = r.ChampionId,
                    Games = r.Games,
                    PlayRate = r.PlayRate,
                }).ToList());
    }

    private async Task<Dictionary<(string Puuid, int ChampionId), ChampionBuild>> FetchTopChampionBuildsAsync(
        Dictionary<string, List<LeaderboardTopChampionReadModel>> topChampionsByPuuid,
        Dictionary<string, Guid> accountIdByPuuid,
        CancellationToken ct)
    {
        // Resolve the page's (account, champion) pairs straight from the top-3
        // result — only the champions actually shown get a build, keeping the
        // dim fetches to the page slice (≤ ~75 pairs) rather than every champion
        // each account has aggregated. The reverse map recovers the puuid from
        // the scope's RiotAccountId (aggregates are keyed by account, not puuid).
        var puuidByAccountId = new Dictionary<Guid, string>(accountIdByPuuid.Count);
        var pairs = new HashSet<(Guid AccountId, int ChampionId)>();
        foreach (var (puuid, champions) in topChampionsByPuuid)
        {
            if (!accountIdByPuuid.TryGetValue(puuid, out var accountId))
            {
                continue;
            }

            puuidByAccountId[accountId] = puuid;
            foreach (var champion in champions)
            {
                pairs.Add((accountId, champion.ChampionId));
            }
        }

        if (pairs.Count == 0)
        {
            return new Dictionary<(string, int), ChampionBuild>();
        }

        // Own short-lived context: this runs concurrently with FetchStats and
        // FetchLatestRanks, and a single DbContext is not thread-safe.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var accountIds = pairs.Select(pair => pair.AccountId).Distinct().ToList();
        var championIds = pairs.Select(pair => pair.ChampionId).Distinct().ToList();
        var queueId = RankedQueueId;

        // Three sequential round trips (one shared context, so not parallel):
        // this aggregate join, then the build-dim lookup, then the rune-dim
        // lookup. This first query joins patterns to the player's ranked-solo
        // scopes for the shown champions, summing each (account, champion, build,
        // runes) combo across every patch / position. Aggregating over all the
        // player's patches mirrors the per-player build pages — the dominant
        // build is the one the player commits to over time, not just on the live
        // patch. The account×champion id filters over-select the cross product,
        // so the exact pairs are re-checked in memory below.
        var grouped = await ctx.ChampionAggregatePatterns
            .AsNoTracking()
            .Join(
                ctx.ChampionAggregateScopes.AsNoTracking()
                    .Where(scope => scope.QueueId == queueId
                        && accountIds.Contains(scope.RiotAccountId)
                        && championIds.Contains(scope.ChampionId)),
                pattern => pattern.ScopeId,
                scope => scope.Id,
                (pattern, scope) => new
                {
                    scope.RiotAccountId,
                    scope.ChampionId,
                    pattern.BuildId,
                    pattern.RunePageId,
                    pattern.Games,
                })
            .GroupBy(row => new { row.RiotAccountId, row.ChampionId, row.BuildId, row.RunePageId })
            .Select(group => new
            {
                group.Key.RiotAccountId,
                group.Key.ChampionId,
                group.Key.BuildId,
                group.Key.RunePageId,
                Games = group.Sum(row => row.Games),
            })
            .ToListAsync(ct);

        // Keep only the (account, champion) pairs the page actually asked for —
        // the SQL filtered each id set independently, so an account that plays
        // champion A and another that plays champion B both pulled rows for A
        // and B; this drops the cross-product leakage.
        var relevant = grouped
            .Where(row => pairs.Contains((row.RiotAccountId, row.ChampionId)))
            .ToList();

        if (relevant.Count == 0)
        {
            return new Dictionary<(string, int), ChampionBuild>();
        }

        var buildIds = relevant.Select(row => row.BuildId).Distinct().ToList();
        var runeIds = relevant.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuilds = await ctx.ChampionDimBuilds.AsNoTracking()
            .Where(dim => buildIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, dim => dim.BuildItem0, ct);
        var dimRunes = await ctx.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, dim => new RunePageDim(dim.PrimaryKeystoneId, dim.SecondaryStyleId), ct);

        var result = new Dictionary<(string Puuid, int ChampionId), ChampionBuild>(relevant.Count);

        foreach (var accountChampion in relevant.GroupBy(row => (row.RiotAccountId, row.ChampionId)))
        {
            if (!puuidByAccountId.TryGetValue(accountChampion.Key.RiotAccountId, out var puuid))
            {
                continue;
            }

            // Hydrate each combo with its dim values, dropping rows whose dim
            // lookup is missing (transient ingest state) or whose first item /
            // keystone is malformed — same guard as LoadTopBuildsAsync.
            var enriched = accountChampion
                .Select(row => new
                {
                    row.Games,
                    FirstItem = dimBuilds.GetValueOrDefault(row.BuildId),
                    Rune = dimRunes.GetValueOrDefault(row.RunePageId),
                })
                .Where(row => row.FirstItem > 0 && row.Rune.PrimaryKeystoneId > 0)
                .ToList();

            if (enriched.Count == 0)
            {
                continue;
            }

            // Dominant (firstItem, keystone) bucket — same tie-break order as
            // LoadTopBuildsAsync (games desc, firstItem asc, keystone asc) so a
            // player's leaderboard cell and their champion page agree.
            var topBucket = enriched
                .GroupBy(row => (FirstItemId: row.FirstItem, KeystoneId: row.Rune.PrimaryKeystoneId))
                .Select(bucket => new
                {
                    FirstItem = bucket.Key.FirstItemId,
                    Keystone = bucket.Key.KeystoneId,
                    Games = bucket.Sum(row => row.Games),
                    Rows = bucket.ToList(),
                })
                .OrderByDescending(bucket => bucket.Games)
                .ThenBy(bucket => bucket.FirstItem)
                .ThenBy(bucket => bucket.Keystone)
                .First();

            // Most-common secondary tree within the winning bucket.
            var secondaryStyleId = topBucket.Rows
                .GroupBy(row => row.Rune.SecondaryStyleId)
                .OrderByDescending(group => group.Sum(row => row.Games))
                .ThenBy(group => group.Key)
                .First().Key;

            result[(puuid, accountChampion.Key.ChampionId)] = new ChampionBuild(
                PrimaryKeystoneId: topBucket.Keystone,
                SecondaryStyleId: secondaryStyleId,
                FirstItemId: topBucket.FirstItem);
        }

        return result;
    }

    private async Task<Dictionary<string, StatsRow>> FetchStatsAsync(
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, StatsRow>();
        }

        // Own short-lived context: this runs concurrently with FetchTopChampions
        // and FetchLatestRanks, and a single DbContext is not thread-safe.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var queue = RankedQueueId;
        // Lifetime ranked-solo stats per puuid. Joining to matches lets us
        // filter by QueueId — match_participants doesn't store the queue
        // itself. CHERRY (Arena) is excluded so historical rows from before
        // the type=ranked ingestor filter don't pollute KDA / W-L.
        FormattableString sql = $"""
            SELECT
                p."Puuid" AS "Puuid",
                COUNT(*)::int AS "Games",
                SUM(CASE WHEN p."Win" THEN 1 ELSE 0 END)::int AS "Wins",
                SUM(CASE WHEN NOT p."Win" THEN 1 ELSE 0 END)::int AS "Losses",
                SUM(p."Kills")::int AS "Kills",
                SUM(p."Deaths")::int AS "Deaths",
                SUM(p."Assists")::int AS "Assists"
            FROM match_participants p
            INNER JOIN matches m ON m."Id" = p."MatchId"
            WHERE p."Puuid" = ANY ({puuids})
              AND m."QueueId" = {queue}
              AND m."GameMode" <> 'CHERRY'
            GROUP BY p."Puuid"
            """;

        var rows = await ctx.Database.SqlQuery<StatsAggregateRow>(sql).ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Puuid,
            r => new StatsRow(
                Games: r.Games,
                Wins: r.Wins,
                Losses: r.Losses,
                Kda: r.Deaths > 0
                    ? (double)(r.Kills + r.Assists) / r.Deaths
                    : r.Kills + r.Assists));
    }

    private async Task<Dictionary<string, LeaderboardPositionsReadModel>> FetchPositionsAsync(
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, LeaderboardPositionsReadModel>();
        }

        // Own short-lived context: this runs concurrently with the other page
        // hydration fetches, and a single DbContext is not thread-safe. The
        // derivation itself lives in MainPositions, shared with search.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        return await MainPositions.FetchAsync(ctx, puuids, ct);
    }

    private static string? NormalizePosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return null;
        }

        var normalized = position.Trim().ToUpperInvariant();
        return normalized switch
        {
            "TOP" or "JUNGLE" or "MIDDLE" or "BOTTOM" or "UTILITY" => normalized,
            _ => null,
        };
    }

    private static LeaderboardResponse Empty(int page, int pageSize) => new()
    {
        Rows = Array.Empty<LeaderboardRowReadModel>(),
        Page = page,
        PageSize = pageSize,
        Total = 0,
    };

    private sealed record PageRow(
        Guid Id,
        string Puuid,
        string GameName,
        string? TagLine,
        string PlatformId,
        int ProfileIconId,
        int SummonerLevel,
        int Score);

    private sealed record RankRow(Guid AccountId, string Tier, string Division, int LeaguePoints);

    private sealed record TopChampionRow(string Puuid, int ChampionId, int Games, double PlayRate);

    // Value type so a missing (puuid, champion) lookup yields all-null build ids
    // via GetValueOrDefault instead of needing a null-reference guard at the
    // call site — null is the contract for "no aggregated build".
    private readonly record struct ChampionBuild(int? PrimaryKeystoneId, int? SecondaryStyleId, int? FirstItemId);

    private readonly record struct RunePageDim(int PrimaryKeystoneId, int SecondaryStyleId);

    private sealed record StatsAggregateRow(
        string Puuid,
        int Games,
        int Wins,
        int Losses,
        int Kills,
        int Deaths,
        int Assists);

    private sealed record StatsRow(int Games, int Wins, int Losses, double Kda);
}
