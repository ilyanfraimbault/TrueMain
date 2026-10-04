using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The Games / KDA / win-rate figures of every account the leaderboard admits,
/// read in one scan so the three stat sorts can order the whole board rather
/// than one page (#1737). The numbers come from the same sources the page
/// hydration shows — the main-champion aggregate scopes for games and KDA, the
/// latest rank snapshot for the win rate — so a row's rank under a stat sort
/// always agrees with the figure printed on it.
/// </summary>
internal static class LeaderboardStatLines
{
    /// <summary>
    /// One line per eligible account. The admission predicate must stay in
    /// lock-step with <see cref="LeaderboardEligibility.CountAsync"/>: the line
    /// count is the sorted board's total.
    /// </summary>
    public static async Task<List<Line>> FetchAsync(
        TrueMainDbContext ctx,
        LeaderboardFilter filter,
        int queueId,
        CancellationToken ct)
    {
        var (platforms, championFilter, position, minGames, otpOnly) = filter;
        var minPositionShare = LeaderboardEligibility.MinPositionShare;

        // Same sums as LeaderboardPageHydration.FetchStatsAsync (mains-only
        // scopes of the ranked queue) and the same latest snapshot as
        // LatestRankSnapshots, over the whole eligible set instead of a page.
        // The latest snapshot is a LATERAL seek on
        // (RiotAccountId, CapturedAtUtc) per account rather than a DISTINCT ON
        // over the whole snapshots table, which holds many rows per account.
        FormattableString sql = $"""
            WITH eligible AS (
                SELECT a."Id", a."Score"
                FROM riot_accounts a
                WHERE a."PlatformId" = ANY ({platforms})
                  AND a."Score" IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM main_champion_stats m
                      WHERE m."PlatformId" = a."PlatformId"
                        AND m."Puuid" = a."Puuid"
                        AND m."IsMain" = true
                        AND m."IsActive" = true
                        AND m."TotalMatches" >= {minGames}
                        AND ({otpOnly}::bool = false OR m."IsOtp" = true)
                        AND ({championFilter}::int IS NULL OR m."ChampionId" = {championFilter})
                        AND ({position}::text IS NULL OR EXISTS (
                            SELECT 1
                            FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                            WHERE pos->>'Position' = {position}
                              AND (pos->>'Rate')::float8 >= {minPositionShare}
                        ))
                  )
            ),
            stats AS (
                SELECT
                    s."RiotAccountId",
                    SUM(s."Games")::int AS "Games",
                    SUM(s."Kills")::bigint AS "Kills",
                    SUM(s."Deaths")::bigint AS "Deaths",
                    SUM(s."Assists")::bigint AS "Assists"
                FROM champion_aggregate_scopes s
                WHERE s."QueueId" = {queueId}
                  AND s."IsMain" = true
                  AND s."RiotAccountId" IN (SELECT "Id" FROM eligible)
                GROUP BY s."RiotAccountId"
            )
            SELECT
                e."Id" AS "AccountId",
                e."Score" AS "Score",
                COALESCE(st."Games", 0) AS "Games",
                COALESCE(st."Kills", 0) AS "Kills",
                COALESCE(st."Deaths", 0) AS "Deaths",
                COALESCE(st."Assists", 0) AS "Assists",
                r."Wins" AS "Wins",
                r."Losses" AS "Losses"
            FROM eligible e
            LEFT JOIN stats st ON st."RiotAccountId" = e."Id"
            LEFT JOIN LATERAL (
                SELECT rs."Wins", rs."Losses"
                FROM rank_snapshots rs
                WHERE rs."RiotAccountId" = e."Id"
                ORDER BY rs."CapturedAtUtc" DESC
                LIMIT 1
            ) r ON true
            """;

        return await ctx.Database.SqlQuery<Line>(sql).ToListAsync(ct);
    }

    /// <summary>
    /// The board's account ids, best first, under one of the stat sorts.
    /// </summary>
    /// <remarks>
    /// Descending on the figure the row prints; an account without one (no
    /// aggregate games yet, or a snapshot without a W-L) sinks to the bottom
    /// rather than ranking as a zero. Ties fall back to the default ranked
    /// standing, then the account id, so paging a tied stretch never repeats
    /// or skips a row.
    /// </remarks>
    public static List<Guid> Order(IReadOnlyList<Line> lines, LeaderboardSort sort)
    {
        Func<Line, double?> metric = sort switch
        {
            LeaderboardSort.Games => line => line.Games > 0 ? line.Games : null,
            LeaderboardSort.Kda => line => line.Games > 0
                ? RateMath.Kda(line.Kills, line.Deaths, line.Assists, line.Games)
                : null,
            LeaderboardSort.WinRate => line => RateMath.WinRate(line.Wins, line.Losses),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Not a stat sort."),
        };

        return lines
            .Select(line => (Line: line, Value: metric(line)))
            .OrderBy(entry => entry.Value is null)
            .ThenByDescending(entry => entry.Value)
            .ThenByDescending(entry => entry.Line.Score)
            .ThenBy(entry => entry.Line.AccountId)
            .Select(entry => entry.Line.AccountId)
            .ToList();
    }

    /// <summary>Whether <paramref name="sort"/> is one of the orders this scan serves.</summary>
    public static bool Serves(LeaderboardSort sort)
        => sort is LeaderboardSort.Games or LeaderboardSort.Kda or LeaderboardSort.WinRate;

    public sealed record Line(
        Guid AccountId,
        int Score,
        int Games,
        long Kills,
        long Deaths,
        long Assists,
        int? Wins,
        int? Losses);
}
