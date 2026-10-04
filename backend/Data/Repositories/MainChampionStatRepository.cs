using Data.Entities;
using Data.Queries;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MainChampionStatRepository(TrueMainDbContext db) : IMainChampionStatRepository
{
    public Task<List<AccountKey>> GetMainAccountsAsync(List<string> platforms, CancellationToken ct)
    {
        return db.MainChampionStats
            .AsNoTracking()
            .Where(s => s.IsMain && s.IsActive && platforms.Contains(s.PlatformId))
            .GroupBy(s => new { s.PlatformId, s.Puuid })
            .Select(g => new AccountKey(g.Key.PlatformId, g.Key.Puuid))
            .ToListAsync(ct);
    }

    public Task<Dictionary<(string PlatformId, int ChampionId), int>> GetMainCountsByPlatformAndChampionAsync(
        CancellationToken ct)
        => ActiveMainCoverageQuery.CountByPlatformAndChampionAsync(db, ct);

    public Task<List<MainChampionStat>> GetByAccountAsync(string platformId, string puuid, CancellationToken ct)
        => db.MainChampionStats
            .AsNoTracking()
            .Where(s => s.PlatformId == platformId && s.Puuid == puuid)
            .ToListAsync(ct);

    public async Task<Dictionary<AccountKey, List<MainChampionStat>>> GetByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        CancellationToken ct)
    {
        var result = new Dictionary<AccountKey, List<MainChampionStat>>();
        if (accounts.Count == 0)
        {
            return result;
        }

        foreach (var grouping in accounts
                     .Distinct()
                     .GroupBy(a => a.PlatformId, StringComparer.OrdinalIgnoreCase))
        {
            var platformId = grouping.Key;
            var puuids = grouping.Select(a => a.Puuid).Distinct(StringComparer.Ordinal).ToList();
            var stats = await db.MainChampionStats
                .Where(s => s.PlatformId == platformId && puuids.Contains(s.Puuid))
                .ToListAsync(ct);

            foreach (var statGroup in stats.GroupBy(s => new AccountKey(s.PlatformId, s.Puuid)))
            {
                result[statGroup.Key] = statGroup.ToList();
            }

            foreach (var account in grouping)
            {
                if (!result.ContainsKey(account))
                {
                    result[account] = [];
                }
            }
        }

        return result;
    }

    public async Task<MatchActivityRecordResult> RecordMatchActivityAsync(
        string platformId,
        IReadOnlyCollection<MatchActivityObservation> observations,
        DateTime activeSinceUtc,
        CancellationToken ct)
    {
        if (observations.Count == 0)
        {
            return new MatchActivityRecordResult(0, 0);
        }

        // Deduplicated here, so one statement per table whatever the batch: the tracked
        // account appears in every one of its matches, and two mains often share a game.
        // Ordered by PUUID so concurrent writers lock overlapping rows in the same order.
        var latestByAccount = observations
            .GroupBy(o => o.Puuid, StringComparer.Ordinal)
            .Select(g => (Puuid: g.Key, PlayedAtUtc: g.Max(o => o.PlayedAtUtc)))
            .OrderBy(o => o.Puuid, StringComparer.Ordinal)
            .ToArray();
        var accountPuuids = latestByAccount.Select(o => o.Puuid).ToArray();
        var accountPlayedAt = latestByAccount
            .Select(o => DateTime.SpecifyKind(o.PlayedAtUtc, DateTimeKind.Utc))
            .ToArray();

        // Conditional on the stamp moving forward, so re-observing an account already
        // stamped by a later game rewrites no row. Restricted to accounts holding a main:
        // the stamp is only ever read by the main-activity selection, and most participants
        // are not mains.
        var accountsStamped = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE "riot_accounts" AS a
            SET "LastSeenInMatchAtUtc" = o.played_at
            FROM unnest({accountPuuids}, {accountPlayedAt}) AS o(puuid, played_at)
            WHERE a."PlatformId" = {platformId}
              AND a."Puuid" = o.puuid
              AND (a."LastSeenInMatchAtUtc" IS NULL OR a."LastSeenInMatchAtUtc" < o.played_at)
              AND EXISTS (
                  SELECT 1 FROM "main_champion_stats" AS s
                  WHERE s."PlatformId" = a."PlatformId" AND s."Puuid" = a."Puuid" AND s."IsMain")
            """,
            ct);

        // Only a game recent enough to count as activity brings a main back: a first
        // ingestion lists weeks of history, and an old game on a dropped champion is
        // exactly what the mastery check deactivated the row for.
        var recentPairs = observations
            .Where(o => o.PlayedAtUtc >= activeSinceUtc)
            .Select(o => (o.Puuid, o.ChampionId))
            .Distinct()
            .OrderBy(o => o.Puuid, StringComparer.Ordinal)
            .ThenBy(o => o.ChampionId)
            .ToArray();
        if (recentPairs.Length == 0)
        {
            return new MatchActivityRecordResult(accountsStamped, 0);
        }

        var pairPuuids = recentPairs.Select(o => o.Puuid).ToArray();
        var pairChampionIds = recentPairs.Select(o => o.ChampionId).ToArray();
        var mainsReactivated = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE "main_champion_stats" AS s
            SET "IsActive" = TRUE
            FROM unnest({pairPuuids}, {pairChampionIds}) AS o(puuid, champion_id)
            WHERE s."PlatformId" = {platformId}
              AND s."Puuid" = o.puuid
              AND s."ChampionId" = o.champion_id
              AND s."IsMain"
              AND NOT s."IsActive"
            """,
            ct);

        return new MatchActivityRecordResult(accountsStamped, mainsReactivated);
    }

    public void Add(MainChampionStat stat)
        => db.MainChampionStats.Add(stat);

    public void Remove(MainChampionStat stat)
        => db.MainChampionStats.Remove(stat);
}
