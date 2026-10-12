using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed partial class RiotAccountRepository
{
    public async Task<int> RecordMatchSummonerProfilesAsync(
        string platformId,
        IReadOnlyCollection<MatchSummonerObservation> observations,
        CancellationToken ct)
    {
        // The newest game per account wins, and Riot reports 0 when it has no icon to give:
        // that is no evidence, so it never replaces one. Ordered by PUUID so concurrent
        // writers lock overlapping rows in the same order.
        var latest = observations
            .Where(o => o.ProfileIconId > 0 && !string.IsNullOrWhiteSpace(o.Puuid))
            .GroupBy(o => o.Puuid, StringComparer.Ordinal)
            .Select(g => g.MaxBy(o => o.PlayedAtUtc)!)
            .OrderBy(o => o.Puuid, StringComparer.Ordinal)
            .ToArray();
        if (latest.Length == 0)
        {
            return 0;
        }

        var puuids = latest.Select(o => o.Puuid).ToArray();
        var iconIds = latest.Select(o => o.ProfileIconId).ToArray();
        var levels = latest.Select(o => o.SummonerLevel).ToArray();
        var playedAt = latest.Select(o => DateTime.SpecifyKind(o.PlayedAtUtc, DateTimeKind.Utc)).ToArray();

        // Conditional on the observation moving forward: a summoner-v4 read stamps its own
        // time, so a match that started before it cannot overwrite what it returned.
        return await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE "riot_accounts" AS a
            SET "ProfileIconId" = o.icon_id,
                "SummonerLevel" = o.level,
                "SummonerObservedAtUtc" = o.played_at
            FROM unnest({puuids}, {iconIds}, {levels}, {playedAt}) AS o(puuid, icon_id, level, played_at)
            WHERE a."PlatformId" = {platformId}
              AND a."Puuid" = o.puuid
              AND (a."SummonerObservedAtUtc" IS NULL OR a."SummonerObservedAtUtc" < o.played_at)
            """,
            ct);
    }
}
