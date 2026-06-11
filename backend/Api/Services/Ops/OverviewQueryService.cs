using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class OverviewQueryService(TrueMainDbContext db) : IOverviewQueryService
{
    public async Task<OverviewReadModel> GetAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);

        var trackedAccounts = await db.RiotAccounts.AsNoTracking().CountAsync(ct);
        var totalMatches = await db.Matches.AsNoTracking().LongCountAsync(ct);
        var totalParticipants = await db.MatchParticipants.AsNoTracking().LongCountAsync(ct);

        var matchesLast7Days = await db.Matches
            .AsNoTracking()
            .Where(match => match.GameStartTimeUtc >= sevenDaysAgo)
            .LongCountAsync(ct);
        var matchesLast30Days = await db.Matches
            .AsNoTracking()
            .Where(match => match.GameStartTimeUtc >= thirtyDaysAgo)
            .LongCountAsync(ct);

        // Group candidate counts server-side, then overlay onto the full set of
        // defined statuses so the map always carries every status (zero-filled),
        // giving the frontend a stable shape regardless of which states exist.
        var candidateCounts = await db.MainCandidates
            .AsNoTracking()
            .GroupBy(candidate => candidate.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var candidatesByStatus = Enum.GetValues<MainCandidateStatus>()
            .ToDictionary(
                status => status.ToString(),
                status => candidateCounts.FirstOrDefault(row => row.Status == status)?.Count ?? 0);

        var totalMains = await db.MainChampionStats
            .AsNoTracking()
            .CountAsync(stat => stat.IsMain, ct);
        var totalOtps = await db.MainChampionStats
            .AsNoTracking()
            .CountAsync(stat => stat.IsOtp, ct);

        var distinctChampionsWithGames = await db.MatchParticipants
            .AsNoTracking()
            .Select(participant => participant.ChampionId)
            .Distinct()
            .CountAsync(ct);
        var distinctChampionsWithMains = await db.MainChampionStats
            .AsNoTracking()
            .Where(stat => stat.IsMain)
            .Select(stat => stat.ChampionId)
            .Distinct()
            .CountAsync(ct);

        return new OverviewReadModel
        {
            TrackedAccounts = trackedAccounts,
            TotalMatches = totalMatches,
            TotalParticipants = totalParticipants,
            CandidatesByStatus = candidatesByStatus,
            TotalMains = totalMains,
            TotalOtps = totalOtps,
            DistinctChampionsWithGames = distinctChampionsWithGames,
            DistinctChampionsWithMains = distinctChampionsWithMains,
            MatchesLast7Days = matchesLast7Days,
            MatchesLast30Days = matchesLast30Days
        };
    }
}
