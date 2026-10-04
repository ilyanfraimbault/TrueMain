using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The latest rank snapshot per account, for the tier / division / LP display
/// cells. Single source for every surface that shows a player's current rank
/// next to their name (leaderboard rows, search results). Runs on the caller's
/// context — callers that hydrate concurrently must pass their own short-lived
/// one (a single DbContext is not thread-safe).
/// </summary>
internal static class LatestRankSnapshots
{
    public static async Task<Dictionary<Guid, Snapshot>> FetchAsync(
        TrueMainDbContext ctx,
        Guid[] accountIds,
        CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return new Dictionary<Guid, Snapshot>();
        }

        // Only the caller's handful of accounts (a page, a search result list)
        // — the heavy ordering already happened elsewhere — so this DISTINCT ON
        // is cheap.
        FormattableString sql = $"""
            SELECT DISTINCT ON (rs."RiotAccountId")
                rs."RiotAccountId" AS "AccountId",
                rs."Tier" AS "Tier",
                rs."Division" AS "Division",
                rs."LeaguePoints" AS "LeaguePoints",
                rs."Wins" AS "Wins",
                rs."Losses" AS "Losses"
            FROM rank_snapshots rs
            WHERE rs."RiotAccountId" = ANY ({accountIds})
            ORDER BY rs."RiotAccountId", rs."CapturedAtUtc" DESC
            """;

        var rows = await ctx.Database.SqlQuery<Snapshot>(sql).ToListAsync(ct);
        return rows.ToDictionary(r => r.AccountId);
    }

    public sealed record Snapshot(Guid AccountId, string Tier, string Division, int LeaguePoints, int? Wins, int? Losses);
}
