using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The per-page reads that turn a slice of eligible accounts into leaderboard
/// rows — the top champions and the main-champion stats — and the projection
/// of one row from everything the page fetched. Every fetch runs on the
/// caller's context: the query service hydrates concurrently, so it passes a
/// short-lived one per fetch.
/// </summary>
internal static class LeaderboardPageHydration
{
    private const int TopChampionsPerRow = 3;

    public static async Task<Dictionary<string, List<LeaderboardTopChampionReadModel>>> FetchTopChampionsAsync(
        TrueMainDbContext ctx,
        string[] platforms,
        string[] puuids,
        CancellationToken ct)
    {
        if (puuids.Length == 0)
        {
            return new Dictionary<string, List<LeaderboardTopChampionReadModel>>();
        }

        var take = TopChampionsPerRow;
        // ROW_NUMBER per puuid keeps the top-3 cap inside the database so we
        // don't fetch every main row only to throw most away in C#. The platform
        // filter adds nothing to the result (a puuid is global) but lets the
        // (PlatformId, Puuid, ChampionId) index seek: on Puuid alone Postgres
        // walks the whole index, 300 ms instead of 1.5 ms for a page (#1570). PlayRate
        // ties tend to coincide with championMatches ties, so the secondary
        // sort matches ProfileQueryService for consistency.
        FormattableString sql = $"""
            WITH ranked AS (
                SELECT
                    m."Puuid" AS "Puuid",
                    m."ChampionId" AS "ChampionId",
                    m."ChampionMatches" AS "Games",
                    m."PlayRate" AS "PlayRate",
                    m."IsOtp" AS "IsOtp",
                    ROW_NUMBER() OVER (
                        PARTITION BY m."Puuid"
                        ORDER BY m."PlayRate" DESC, m."ChampionMatches" DESC
                    ) AS rn
                FROM main_champion_stats m
                WHERE m."PlatformId" = ANY ({platforms})
                  AND m."Puuid" = ANY ({puuids})
                  AND m."IsMain" = true
                  AND m."IsActive" = true
            )
            SELECT "Puuid", "ChampionId", "Games", "PlayRate", "IsOtp"
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
                    IsOtp = r.IsOtp,
                }).ToList());
    }

    public static async Task<Dictionary<string, StatsRow>> FetchStatsAsync(
        TrueMainDbContext ctx,
        int queueId,
        Guid[] accountIds,
        IReadOnlyDictionary<Guid, string> puuidByAccountId,
        CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return new Dictionary<string, StatsRow>();
        }

        // Games / W-L / KDA summed from the frozen per-champion aggregate scopes
        // (champion_aggregate_scopes), not live match_participants. Retention
        // hard-deletes participants beyond the last couple of patches, which
        // zeroed this cell for players whose recent ranked games have aged out
        // (a #1-LP main could read "0 games"); scopes persist because old
        // patches are frozen, so the numbers stay stable — and one indexed SUM
        // is cheaper than the old join over participants.
        //
        // Each scope row is already one (champion, patch, position, elo bracket)
        // slice and every match maps to exactly one row, so a straight SUM never
        // double-counts. The synthetic ALL bracket is a read-time union that is
        // never stored, so summing every persisted row for the account is its
        // total. This covers the player's main champions only (the aggregation
        // source is gated on IsMain) — off-champion ranked games are out of
        // scope by design, matching the mains-centric top-champions cell.
        var rows = await ctx.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId && accountIds.Contains(scope.RiotAccountId))
            // Mains only — see #1346. The comment above already says this total is
            // "gated on IsMain"; that used to be true of the source rows, and is
            // now true because the read says so.
            .Where(scope => scope.IsMain)
            .GroupBy(scope => scope.RiotAccountId)
            .Select(group => new
            {
                RiotAccountId = group.Key,
                Games = group.Sum(scope => scope.Games),
                Wins = group.Sum(scope => scope.Wins),
                Kills = group.Sum(scope => scope.Kills),
                Deaths = group.Sum(scope => scope.Deaths),
                Assists = group.Sum(scope => scope.Assists),
            })
            .ToListAsync(ct);

        var result = new Dictionary<string, StatsRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!puuidByAccountId.TryGetValue(row.RiotAccountId, out var puuid))
            {
                continue;
            }

            result[puuid] = new StatsRow(
                Games: row.Games,
                Wins: row.Wins,
                Losses: row.Games - row.Wins,
                Kda: RateMath.Kda(row.Kills, row.Deaths, row.Assists, row.Games));
        }

        return result;
    }

    /// <summary>
    /// Projects one leaderboard row from the page's identity cells and the
    /// hydration lookups. Every lookup is optional: a cell whose data the
    /// pipeline hasn't produced yet degrades to an empty / zero value instead
    /// of dropping the row.
    /// </summary>
    public static LeaderboardRowReadModel BuildRow(
        int rank,
        LeaderboardEligibility.PageRow row,
        List<LeaderboardTopChampionReadModel>? topChampions,
        IReadOnlyDictionary<(string Puuid, int ChampionId), LeaderboardTopBuilds.ChampionBuild> buildsByPuuidChampion,
        StatsRow? stats,
        LatestRankSnapshots.Snapshot? latestRank,
        LeaderboardPositionsReadModel? positions,
        DedicationReadModel? dedication)
    {
        // Enrich each top champion with the player's dominant build. The
        // build is absent for champions the aggregate pipeline hasn't
        // produced a pattern for yet — the three ids stay null then, never
        // throwing (GetValueOrDefault returns the struct's default).
        var topChamps = (topChampions ?? new List<LeaderboardTopChampionReadModel>())
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

        // platformId → region slug. RouteToSlug can return null for
        // platforms we don't expose (JP1/SEA); the platforms filter
        // already excluded them, so this is just a defensive default.
        var regionSlug = RegionFilterParser.RouteToSlug(row.PlatformId) ?? string.Empty;

        return new LeaderboardRowReadModel
        {
            Rank = rank,
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
                // WR / W-L are the player's overall ranked split totals from the
                // latest rank snapshot (League-V4 Wins/Losses), not the
                // main-champion aggregate. A main whose tracked-champion games
                // have aged out of champion_aggregate_scopes (or whose displayed
                // main is a stale snapshot they no longer play) still has a live
                // ranked W-L on their snapshot, so the WR cell stays populated
                // instead of vanishing. Games / KDA remain the main-champion
                // aggregate (games on tracked mains), so games and W+L can differ.
                Wins = latestRank?.Wins,
                Losses = latestRank?.Losses,
                WinRate = RateMath.WinRate(latestRank?.Wins, latestRank?.Losses),
                Kda = stats?.Kda,
            },
            TopChampions = topChamps,
            Positions = positions,
            Dedication = dedication,
        };
    }

    public sealed record StatsRow(int Games, int Wins, int Losses, double Kda);

    private sealed record TopChampionRow(string Puuid, int ChampionId, int Games, double PlayRate, bool IsOtp);
}
