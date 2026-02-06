using Data;
using Data.Entities;
using Ingestor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class ScoringProcess(
    ILogger<ScoringProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<ScoringOptions> scoringOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var scoring = scoringOptions.Value;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var candidates = await db.MainCandidates
            .Where(c => c.Status == MainCandidateStatus.New)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            logger.LogInformation("No new candidates to score.");
            return;
        }

        foreach (var candidate in candidates)
        {
            candidate.Score = ComputeScore(candidate, scoring, nowUtc);
            candidate.Status = MainCandidateStatus.Scored;
            candidate.ScoredAtUtc = nowUtc;
        }

        LogPendingChanges(logger, db, "Scoring", "Score");
        await db.SaveChangesAsync(ct);

        var grouped = candidates
            .GroupBy(c => c.PlatformId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in grouped)
        {
            var queued = await db.MainCandidates
                .Where(c => c.PlatformId == group.Key && c.Status == MainCandidateStatus.Scored)
                .OrderByDescending(c => c.Score)
                .Take(Math.Max(0, scoring.TopNPerPlatform))
                .ToListAsync(ct);

            foreach (var candidate in queued)
            {
                candidate.Status = MainCandidateStatus.Queued;
            }

            LogPendingChanges(logger, db, "Scoring", $"Queue {group.Key}");
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Scoring summary for {Platform}: scored={Scored}, queued={Queued}.",
                group.Key,
                group.Count(),
                queued.Count);
        }
    }

    private static double ComputeScore(MainCandidate candidate, ScoringOptions scoring, DateTime nowUtc)
    {
        var maxLastPlayDays = scoring.MaxLastPlayDays <= 0 ? 1 : scoring.MaxLastPlayDays;
        var topN = scoring.TopChampionsPerAccount <= 0 ? 10 : scoring.TopChampionsPerAccount;

        var recencyDays = Math.Max(0, (nowUtc - candidate.LastPlayTimeUtc).TotalDays);
        var recencyScore = Clamp(1 - recencyDays / maxLastPlayDays, 0, 1);

        var rankScore = (topN + 1 - candidate.ChampionRankInMasteryTop) / (double)topN;
        rankScore = Clamp(rankScore, 0, 1);

        var pointsScore = Clamp(Math.Log10(candidate.ChampionPoints + 1) / 6, 0, 1);

        return 100 * (0.5 * recencyScore + 0.3 * rankScore + 0.2 * pointsScore);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static void LogPendingChanges(
        ILogger logger,
        TrueMainDbContext db,
        string stage,
        string detail)
    {
        var added = 0;
        var modified = 0;
        var deleted = 0;

        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    added++;
                    break;
                case EntityState.Modified:
                    modified++;
                    break;
                case EntityState.Deleted:
                    deleted++;
                    break;
            }
        }

        if (added == 0 && modified == 0 && deleted == 0)
        {
            return;
        }

        logger.LogDebug(
            "{Stage} DB changes ({Detail}): added={Added}, modified={Modified}, deleted={Deleted}.",
            stage,
            detail,
            added,
            modified,
            deleted);
    }
}
