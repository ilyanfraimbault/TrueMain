using Core.Lol.Map;
using Core.Truemains;
using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// Every account the leaderboard can show and their mains, read once per aggregation version, so
/// any combination of filters is a walk in memory (#2425). Each sort used to re-run its eligibility
/// over the whole population per filter shape, at seconds a shape, and a shape rarely came up twice
/// within its cache. The predicate lives here once: every filter lands on the same
/// <c>main_champion_stats</c> row, so <c>?championId=X&amp;position=Y</c> means "has an X main played
/// in Y".
/// </summary>
internal sealed class LeaderboardPopulation
{
    // Best ranked first, and their mains side by side.
    private readonly Account[] accounts;
    private readonly Main[] mains;
    private readonly List<string> platforms;

    internal LeaderboardPopulation(IReadOnlyList<Row> rankOrderedRows)
    {
        var accountList = new List<Account>();
        var platformIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        platforms = [];
        mains = new Main[rankOrderedRows.Count];
        for (var i = 0; i < rankOrderedRows.Count; i++)
        {
            var row = rankOrderedRows[i];
            mains[i] = new Main(
                row.ChampionId, row.TotalMatches, row.IsOtp, row.Positions, row.PlayRate, row.ChampionMatches,
                row.MasteryPoints, row.MasteryRank);
            if (accountList.Count > 0 && accountList[^1].Id == row.AccountId)
            {
                accountList[^1] = accountList[^1] with { MainCount = accountList[^1].MainCount + 1 };
                continue;
            }

            if (!platformIndex.TryGetValue(row.PlatformId, out var platform))
            {
                platform = platforms.Count;
                platformIndex[row.PlatformId] = platform;
                platforms.Add(row.PlatformId);
            }

            accountList.Add(new Account(row.AccountId, platform, i, 1));
        }

        accounts = [.. accountList];
    }

    /// <summary>Reads the whole population, in the order the default leaderboard ranks it.</summary>
    public static async Task<LeaderboardPopulation> LoadAsync(TrueMainDbContext db, CancellationToken ct)
    {
        var minPositionShare = LeaderboardEligibility.MinPositionShare;
        FormattableString sql = $"""
            SELECT
                a."Id" AS "AccountId",
                a."PlatformId" AS "PlatformId",
                m."ChampionId" AS "ChampionId",
                m."TotalMatches" AS "TotalMatches",
                m."IsOtp" AS "IsOtp",
                (SELECT COALESCE(bit_or(CASE pos->>'Position'
                            WHEN 'TOP' THEN 1
                            WHEN 'JUNGLE' THEN 2
                            WHEN 'MIDDLE' THEN 4
                            WHEN 'BOTTOM' THEN 8
                            WHEN 'UTILITY' THEN 16
                            ELSE 0 END), 0)
                 FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                 WHERE (pos->>'Rate')::float8 >= {minPositionShare}) AS "Positions",
                m."PlayRate" AS "PlayRate",
                m."ChampionMatches" AS "ChampionMatches",
                m."MasteryPoints" AS "MasteryPoints",
                m."MasteryRank" AS "MasteryRank"
            FROM riot_accounts a
            JOIN main_champion_stats m
              ON m."PlatformId" = a."PlatformId" AND m."Puuid" = a."Puuid"
            WHERE a."Score" IS NOT NULL
              AND m."IsMain" = true
              AND m."IsActive" = true
            ORDER BY a."Score" DESC, a."Id"
            """;

        return new LeaderboardPopulation(await db.Database.SqlQuery<Row>(sql).ToListAsync(ct));
    }

    /// <summary>The accounts <paramref name="filter"/> admits, in the default leaderboard's order.</summary>
    public List<Guid> Select(LeaderboardFilter filter)
    {
        var platformSelected = PlatformsOf(filter);
        var positions = PositionBit(filter.Position);
        var selected = new List<Guid>();
        foreach (ref readonly var account in accounts.AsSpan())
        {
            if (platformSelected[account.Platform] && HasMatchingMain(account, filter, positions))
            {
                selected.Add(account.Id);
            }
        }

        return selected;
    }

    /// <summary>
    /// The accounts <paramref name="filter"/> admits, best dedication first. Scored on each account's
    /// signature champion, the highest play rate among its mains: only the champion filter re-points
    /// it, as <see cref="MainDedication"/> explains, so a lane filter never changes a score. A tie falls
    /// back on the account id, so paging a tied stretch never repeats or skips a row.
    /// </summary>
    public List<Guid> RankByDedication(LeaderboardFilter filter, double commitmentFloor)
    {
        var platformSelected = PlatformsOf(filter);
        var positions = PositionBit(filter.Position);
        var scored = new List<(Guid AccountId, double Score)>();
        foreach (ref readonly var account in accounts.AsSpan())
        {
            if (platformSelected[account.Platform] && HasMatchingMain(account, filter, positions))
            {
                var main = SignatureMain(account, filter.ChampionId);
                var inputs = new DedicationInputs(main.PlayRate, main.MasteryPoints, main.MasteryRank);
                scored.Add((account.Id, DedicationScore.Compute(inputs, commitmentFloor).Score));
            }
        }

        return
        [
            .. scored
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.AccountId)
                .Select(entry => entry.AccountId),
        ];
    }

    private bool[] PlatformsOf(LeaderboardFilter filter)
    {
        var selected = new bool[platforms.Count];
        for (var platform = 0; platform < platforms.Count; platform++)
        {
            selected[platform] = filter.Platforms.Contains(platforms[platform], StringComparer.Ordinal);
        }

        return selected;
    }

    private bool HasMatchingMain(in Account account, LeaderboardFilter filter, int positions)
    {
        foreach (ref readonly var main in mains.AsSpan(account.FirstMain, account.MainCount))
        {
            if (main.TotalMatches >= filter.MinGames
                && (!filter.OtpOnly || main.IsOtp)
                && (filter.ChampionId is not { } champion || main.ChampionId == champion)
                && (positions == AnyPosition || (main.Positions & positions) != 0))
            {
                return true;
            }
        }

        return false;
    }

    private Main SignatureMain(in Account account, int? championId)
    {
        var signature = default(Main);
        var found = false;
        foreach (ref readonly var main in mains.AsSpan(account.FirstMain, account.MainCount))
        {
            if (championId is { } champion && main.ChampionId != champion)
            {
                continue;
            }

            if (!found
                || main.PlayRate > signature.PlayRate
                || (main.PlayRate.Equals(signature.PlayRate) && main.ChampionMatches > signature.ChampionMatches))
            {
                signature = main;
                found = true;
            }
        }

        return signature;
    }

    private const int AnyPosition = -1;

    // A position the leaderboard does not know selects nothing, as it did in SQL.
    private static int PositionBit(string? position)
    {
        if (position is null)
        {
            return AnyPosition;
        }

        var index = LanePositions.All.ToList().IndexOf(position);
        return index < 0 ? 0 : 1 << index;
    }

    /// <summary>One main of an eligible account, as the population's query returns it.</summary>
    internal sealed record Row(
        Guid AccountId,
        string PlatformId,
        int ChampionId,
        int TotalMatches,
        bool IsOtp,
        int Positions,
        double PlayRate,
        int ChampionMatches,
        long? MasteryPoints,
        int? MasteryRank);

    private readonly record struct Account(Guid Id, int Platform, int FirstMain, int MainCount);

    private readonly record struct Main(
        int ChampionId,
        int TotalMatches,
        bool IsOtp,
        int Positions,
        double PlayRate,
        int ChampionMatches,
        long? MasteryPoints,
        int? MasteryRank);
}
