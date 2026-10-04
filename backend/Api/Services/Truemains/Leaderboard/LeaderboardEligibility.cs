using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// Which accounts the leaderboard admits, and the identity cells of a page of
/// them: the eligible count, the rank-ordered page, and the by-id page the
/// dedication ranking slices. Runs on the caller's context.
/// </summary>
internal static class LeaderboardEligibility
{
    // A position filter surfaces every player who plays that position on a
    // main champion at least this share of the time, not just players whose
    // single most-played position matches. The bar is low (20%) on purpose:
    // any meaningful flex into the lane should make the player visible there,
    // and only one-off cameos (<20% of a champion's games) stay filtered out.
    // Shared with the primary/secondary derivation in MainPositions so the
    // filter bar and the "secondary lane" bar can't drift apart.
    public const double MinPositionShare = MainPositions.MinShare;

    public static async Task<int> CountAsync(TrueMainDbContext db, LeaderboardFilter filter, CancellationToken ct)
    {
        var (platforms, championFilter, position, minGames, otpOnly) = filter;

        // The /truemains leaderboard is, by definition, the list of truemains
        // — so the `IsMain = true` EXISTS is unconditional. Accounts that
        // haven't been through main analysis yet (fresh ingests) are out of
        // scope until they do. The `championFilter` / `position` / `otpOnly`
        // parameters degrade to "any champion / any position / any main" so they
        // compose inside the same EXISTS clause without an outer toggle. When
        // `otpOnly` is set with a champion filter, both land on the same
        // main_champion_stats row, so it means "OTP of that champion".
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
                    AND m."IsActive" = true
                    AND m."TotalMatches" >= {minGames}
                    AND ({otpOnly}::bool = false OR m."IsOtp" = true)
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

    public static async Task<List<PageRow>> FetchPageAsync(
        TrueMainDbContext db,
        LeaderboardFilter filter,
        int offset,
        int pageSize,
        CancellationToken ct)
    {
        var (platforms, championFilter, position, minGames, otpOnly) = filter;

        // Order + paginate directly on the denormalised riot_accounts."Score"
        // (maintained by RankSnapshotWriter) — no DISTINCT ON, no inline score
        // CASE. "Score IS NOT NULL" is the is-ranked gate and must stay in
        // lock-step with CountAsync or pagination drifts from the total. The
        // tier/div/LP display cells are hydrated per page by the query service.
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
                    AND m."IsActive" = true
                    AND m."TotalMatches" >= {minGames}
                    AND ({otpOnly}::bool = false OR m."IsOtp" = true)
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

    /// <summary>
    /// Identity cells for an already-ordered slice of accounts. Used by the
    /// dedication ranking, whose ordering lives in memory (the score is derived
    /// at read time, so there is no column to page on): SQL fetches the ~25 rows
    /// by primary key and C# restores the caller's order.
    /// </summary>
    public static async Task<List<PageRow>> FetchPageByAccountIdsAsync(
        TrueMainDbContext db,
        Guid[] accountIds,
        CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return [];
        }

        // `Score IS NOT NULL` is re-asserted even though the candidate scan
        // already vetted these ids: that scan was a separate round trip, and an
        // account whose rank snapshot is cleared in between would materialise a
        // SQL NULL into PageRow's non-nullable int and throw. Re-stating the
        // predicate turns that race into a dropped row, which the reordering
        // below already tolerates — same reason FetchPageAsync carries it.
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
            WHERE a."Id" = ANY ({accountIds})
              AND a."Score" IS NOT NULL
            """;

        var rows = await db.Database.SqlQuery<PageRow>(sql).ToListAsync(ct);
        var byId = rows.ToDictionary(row => row.Id);

        // Preserve the ranking order; an id that vanished between the candidate
        // scan and this fetch (account deleted or unranked mid-request) is
        // simply dropped.
        return accountIds
            .Select(id => byId.GetValueOrDefault(id))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();
    }

    public sealed record PageRow(
        Guid Id,
        string Puuid,
        string GameName,
        string? TagLine,
        string PlatformId,
        int ProfileIconId,
        int SummonerLevel,
        int Score);
}
