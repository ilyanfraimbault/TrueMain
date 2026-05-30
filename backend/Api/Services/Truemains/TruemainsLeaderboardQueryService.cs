using System.Diagnostics;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public sealed class TruemainsLeaderboardQueryService(
    TrueMainDbContext db,
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

    // Ranked solo queue id (420). Matches the queue used by MainStatsCalculator
    // for main_champion_stats, so the "games" / KDA / winrate cell stays
    // consistent with the player's top-champions cell on the same row.
    private const int RankedQueueId = 420;

    // A position filter surfaces every player who plays that position on a
    // main champion at least this share of the time, not just players whose
    // single most-played position matches. The bar is low (20%) on purpose:
    // any meaningful flex into the lane should make the player visible there,
    // and only one-off cameos (<20% of a champion's games) stay filtered out.
    private const double MinPositionShare = 0.2d;

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

        var total = await CountAsync(platforms, championFilter, normalizedPosition, minGames, ct);
        if (total == 0)
        {
            var empty = Empty(clampedPage, clampedPageSize);
            // Cache the empty response — a filter that yields nothing still
            // pays for the Count SQL on every visit, and those are the same
            // requests an attacker / overzealous client would replay.
            cache.Set(cacheKey, empty, ResponseCacheTtl);
            return empty;
        }

        var pageRows = await FetchPageAsync(
            platforms, championFilter, normalizedPosition, minGames, offset, clampedPageSize, ct);
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
            cache.Set(cacheKey, pastEnd, ResponseCacheTtl);
            return pastEnd;
        }

        // Hydrate the page slice with derived stats. Two batched queries — one
        // for the top-3 champions (main_champion_stats), one for KDA / W-L
        // (match_participants joined to matches for the queue filter). Both
        // are keyed by puuid because that's the natural identifier for those
        // tables (RiotAccountId is nullable on match_participants).
        var puuids = pageRows.Select(r => r.Puuid).ToArray();
        var topChampionsByPuuid = await FetchTopChampionsAsync(puuids, ct);
        var statsByPuuid = await FetchStatsAsync(puuids, ct);

        var rank = offset + 1;
        var rows = new List<LeaderboardRowReadModel>(pageRows.Count);
        foreach (var row in pageRows)
        {
            var topChamps = topChampionsByPuuid.GetValueOrDefault(row.Puuid)
                            ?? new List<LeaderboardTopChampionReadModel>();
            var stats = statsByPuuid.GetValueOrDefault(row.Puuid);

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
                    Tier = row.Tier,
                    Division = row.Division,
                    LeaguePoints = row.LeaguePoints,
                    Score = row.Score,
                },
                Stats = new LeaderboardStatsReadModel
                {
                    Games = stats?.Games ?? 0,
                    Wins = stats?.Wins,
                    Losses = stats?.Losses,
                    WinRate = stats is not null
                        ? ComputeWinRate(stats.Wins, stats.Losses)
                        : null,
                    Kda = stats?.Kda,
                },
                TopChampions = topChamps,
            });
        }

        var response = new LeaderboardResponse
        {
            Rows = rows,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = total,
        };
        cache.Set(cacheKey, response, ResponseCacheTtl);

        totalSw.Stop();
        logger.LogInformation(
            "[truemain-leaderboard] page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} rows={Rows} total={Total} elapsed={ElapsedMs}ms result=miss",
            clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames,
            rows.Count, total, totalSw.ElapsedMilliseconds);

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
        FormattableString sql = $"""
            SELECT COUNT(*)::int AS "Value"
            FROM riot_accounts a
            WHERE a."PlatformId" = ANY ({platforms})
              AND EXISTS (
                  SELECT 1 FROM rank_snapshots rs
                  WHERE rs."RiotAccountId" = a."Id"
              )
              AND EXISTS (
                  SELECT 1 FROM main_champion_stats m
                  WHERE m."PlatformId" = a."PlatformId"
                    AND m."Puuid" = a."Puuid"
                    AND m."IsMain" = true
                    AND ({championFilter}::int IS NULL OR m."ChampionId" = {championFilter})
                    AND ({position}::text IS NULL OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                        WHERE pos->>'Position' = {position}
                          AND (pos->>'Rate')::float8 >= {MinPositionShare}
                    ))
              )
              AND ({minGames} = 0 OR (
                  SELECT COUNT(*)::int
                  FROM match_participants p
                  INNER JOIN matches m ON m."Id" = p."MatchId"
                  WHERE p."Puuid" = a."Puuid"
                    AND m."QueueId" = {RankedQueueId}
                    AND m."GameMode" <> 'CHERRY'
              ) >= {minGames})
            """;

        return await db.Database.SqlQuery<int>(sql).FirstAsync(ct);
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
        // DISTINCT ON gives us the latest snapshot per account in a single
        // pass (uses IX_rank_snapshots_account_captured). INNER JOIN drops
        // never-ranked accounts — those are out of scope for V1. The score
        // CASE is computed inline so ORDER BY happens server-side over the
        // full filtered set, allowing real pagination.
        //
        // The IsMain=true EXISTS is unconditional — see CountAsync for the
        // rationale. The two EXISTS clauses (CountAsync + FetchPageAsync)
        // must stay in lock-step or pagination will drift from the total.
        var scoreExpression = RankScore.OrderBySqlFragment("ls");

        // Use FormattableStringFactory to compose the SQL: scoreExpression is
        // raw SQL (constants only — no user input) that must NOT be bound as
        // a parameter, so it goes into the literal portion of the format
        // string, while the actual parameters keep their interpolation order.
        var sqlTemplate = $$"""
            WITH latest_snapshot AS (
                SELECT DISTINCT ON ("RiotAccountId")
                    "RiotAccountId", "Tier", "Division", "LeaguePoints", "Wins", "Losses"
                FROM rank_snapshots
                ORDER BY "RiotAccountId", "CapturedAtUtc" DESC
            )
            SELECT
                a."Id" AS "Id",
                a."Puuid" AS "Puuid",
                a."GameName" AS "GameName",
                a."TagLine" AS "TagLine",
                a."PlatformId" AS "PlatformId",
                a."ProfileIconId" AS "ProfileIconId",
                a."SummonerLevel" AS "SummonerLevel",
                ls."Tier" AS "Tier",
                ls."Division" AS "Division",
                ls."LeaguePoints" AS "LeaguePoints",
                {{scoreExpression}} AS "Score"
            FROM riot_accounts a
            INNER JOIN latest_snapshot ls ON ls."RiotAccountId" = a."Id"
            WHERE a."PlatformId" = ANY ({0})
              AND EXISTS (
                  SELECT 1 FROM main_champion_stats m
                  WHERE m."PlatformId" = a."PlatformId"
                    AND m."Puuid" = a."Puuid"
                    AND m."IsMain" = true
                    AND ({1}::int IS NULL OR m."ChampionId" = {1})
                    AND ({2}::text IS NULL OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                        WHERE pos->>'Position' = {2}
                          AND (pos->>'Rate')::float8 >= {6}
                    ))
              )
              AND ({5} = 0 OR (
                  SELECT COUNT(*)::int
                  FROM match_participants p
                  INNER JOIN matches m ON m."Id" = p."MatchId"
                  WHERE p."Puuid" = a."Puuid"
                    AND m."QueueId" = {{RankedQueueId}}
                    AND m."GameMode" <> 'CHERRY'
              ) >= {5})
            ORDER BY "Score" DESC NULLS LAST, a."Id"
            LIMIT {3} OFFSET {4}
            """;

        var sql = System.Runtime.CompilerServices.FormattableStringFactory.Create(
            sqlTemplate,
            platforms,
            (object?)championFilter ?? DBNull.Value,
            (object?)position ?? DBNull.Value,
            pageSize,
            offset,
            minGames,
            MinPositionShare);

        return await db.Database.SqlQuery<PageRow>(sql).ToListAsync(ct);
    }

    private async Task<Dictionary<string, List<LeaderboardTopChampionReadModel>>> FetchTopChampionsAsync(
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, List<LeaderboardTopChampionReadModel>>();
        }

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
                    ROW_NUMBER() OVER (
                        PARTITION BY m."Puuid"
                        ORDER BY m."PlayRate" DESC, m."ChampionMatches" DESC
                    ) AS rn
                FROM main_champion_stats m
                WHERE m."Puuid" = ANY ({puuids})
                  AND m."IsMain" = true
            )
            SELECT "Puuid", "ChampionId", "Games"
            FROM ranked
            WHERE rn <= {take}
            ORDER BY "Puuid", rn
            """;

        var rows = await db.Database.SqlQuery<TopChampionRow>(sql).ToListAsync(ct);

        return rows
            .GroupBy(r => r.Puuid)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new LeaderboardTopChampionReadModel
                {
                    ChampionId = r.ChampionId,
                    Games = r.Games,
                }).ToList());
    }

    private async Task<Dictionary<string, StatsRow>> FetchStatsAsync(
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, StatsRow>();
        }

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

        var rows = await db.Database.SqlQuery<StatsAggregateRow>(sql).ToListAsync(ct);

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

    private static double? ComputeWinRate(int? wins, int? losses)
    {
        if (wins is null || losses is null)
        {
            return null;
        }

        var total = wins.Value + losses.Value;
        return total == 0 ? null : (double)wins.Value / total;
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
        string Tier,
        string Division,
        int LeaguePoints,
        int Score);

    private sealed record TopChampionRow(string Puuid, int ChampionId, int Games);

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
