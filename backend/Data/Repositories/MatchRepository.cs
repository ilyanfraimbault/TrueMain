using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchRepository(TrueMainDbContext db) : IMatchRepository
{
    public async Task<HashSet<string>> GetExistingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<HashSet<string>> GetTimelinePendingMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var pendingIds = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id) && !m.TimelineIngested)
            .Select(m => m.Id)
            .ToListAsync(ct);

        return pendingIds.ToHashSet(StringComparer.Ordinal);
    }

    public Task SetTimelineIngestedAsync(IReadOnlyCollection<string> matchIds, bool timelineIngested, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.TimelineIngested, timelineIngested),
                ct);
    }

    public void Add(Match match)
        => db.Matches.Add(match);

    public Task<int> InvalidatePatternAggregationAsync(
        IReadOnlyCollection<MainChampionKey> mains,
        CancellationToken ct)
    {
        if (mains.Count == 0)
        {
            return Task.FromResult(0);
        }

        var platformIds = mains.Select(main => main.PlatformId).ToArray();
        var puuids = mains.Select(main => main.Puuid).ToArray();
        var championIds = mains.Select(main => main.ChampionId).ToArray();

        // Driven from the keys: IX_match_participants_puuid_match finds each player's rows,
        // the primary key their matches.
        return db.Database.ExecuteSqlAsync(
            $"""
            UPDATE "matches" AS m
            SET "PatternAggregated" = false
            FROM unnest({platformIds}, {puuids}, {championIds}) AS k("PlatformId", "Puuid", "ChampionId")
            JOIN "match_participants" AS p
                ON p."Puuid" = k."Puuid" AND p."ChampionId" = k."ChampionId"
            WHERE m."Id" = p."MatchId"
                AND m."PlatformId" = k."PlatformId"
                AND m."PatternAggregated"
            """,
            ct);
    }

    public async Task<List<PaceBenchmarkFoldClaim>> ClaimPaceBenchmarkFoldAsync(
        IReadOnlyCollection<string> matchIds,
        CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return [];
        }

        var ids = matchIds.Distinct(StringComparer.Ordinal).ToArray();

        return await db.Database.SqlQuery<PaceBenchmarkFoldClaim>(
                $"""
                 UPDATE matches
                 SET "PaceBenchmarkAggregated" = true
                 WHERE "Id" = ANY({ids}) AND NOT "PaceBenchmarkAggregated"
                 RETURNING "Id", "Patch", "GameStartTimeUtc", "GameDurationSeconds", "EndedInEarlySurrender"
                 """)
            .ToListAsync(ct);
    }
}
