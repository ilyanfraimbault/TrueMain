using Data;
using Data.Entities;
using Ingestor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class MainAnalysisProcess(
    ILogger<MainAnalysisProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<MainAnalysisOptions> analysisOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = analysisOptions.Value;
        var batchSize = Math.Max(1, options.BatchSize);
        var matchesToConsider = Math.Max(1, options.MatchesToConsider);
        var queueId = options.QueueId;
        var nowUtc = DateTime.UtcNow;

        var cutoff = options.RecomputeAfterHours > 0
            ? nowUtc.AddHours(-options.RecomputeAfterHours)
            : DateTime.MinValue;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var accountQuery =
            from account in db.RiotAccounts
            join candidate in db.MainCandidates
                on new { account.PlatformId, account.Puuid } equals new { candidate.PlatformId, candidate.Puuid }
            where candidate.Status == MainCandidateStatus.Validated
            select account;

        if (options.RecomputeAfterHours > 0)
        {
            accountQuery = accountQuery
                .Where(a => a.LastMainCalcAtUtc == null || a.LastMainCalcAtUtc < cutoff);
        }

        var accounts = await accountQuery
            .Distinct()
            .OrderBy(a => a.LastMainCalcAtUtc == null ? 0 : 1)
            .ThenBy(a => a.LastMainCalcAtUtc)
            .Take(batchSize)
            .Select(a => new AccountKey(a.PlatformId, a.Puuid))
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            logger.LogInformation("No accounts eligible for main analysis.");
            return;
        }

        var processed = 0;
        var totalStatsUpserted = 0;
        var totalStatsRemoved = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            await using var accountDb = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await accountDb.Database.BeginTransactionAsync(ct);

            var participantRows = await (
                    from participant in accountDb.MatchParticipants
                    join match in accountDb.Matches on participant.MatchId equals match.Id
                    where participant.Puuid == account.Puuid &&
                          match.PlatformId == account.PlatformId &&
                          match.QueueId == queueId
                    orderby match.GameStartTimeUtc descending
                    select new ParticipantRow(participant.ChampionId, participant.TeamPosition)
                )
                .Take(matchesToConsider)
                .ToListAsync(ct);

            var validParticipants = participantRows
                .Where(p => IsValidTeamPosition(p.TeamPosition))
                .Select(p => new ParticipantRow(p.ChampionId, NormalizePosition(p.TeamPosition)))
                .ToList();

            var totalMatches = validParticipants.Count;

            var existingStats = await accountDb.MainChampionStats
                .Where(s => s.PlatformId == account.PlatformId && s.Puuid == account.Puuid)
                .ToListAsync(ct);

            var statsByChampion = existingStats.ToDictionary(s => s.ChampionId);
            var newStats = new List<MainChampionStat>();

            if (totalMatches > 0)
            {
                foreach (var group in validParticipants.GroupBy(p => p.ChampionId))
                {
                    var championMatches = group.Count();
                    var playRate = (double)championMatches / totalMatches;

                    var positions = group
                        .GroupBy(p => p.TeamPosition)
                        .Select(g =>
                        {
                            var games = g.Count();
                            return new PositionStat
                            {
                                Position = g.Key,
                                Games = games,
                                Rate = championMatches == 0 ? 0 : (double)games / championMatches
                            };
                        })
                        .OrderByDescending(p => p.Games)
                        .ToList();

                    var primaryPosition = positions.Count > 0 ? positions[0].Position : string.Empty;
                    var isMain = totalMatches >= options.MinMatchesToEvaluate &&
                                 playRate >= options.PlayRateThreshold;

                    newStats.Add(new MainChampionStat
                    {
                        PlatformId = account.PlatformId,
                        Puuid = account.Puuid,
                        ChampionId = group.Key,
                        TotalMatches = totalMatches,
                        ChampionMatches = championMatches,
                        PlayRate = playRate,
                        IsMain = isMain,
                        PrimaryPosition = primaryPosition,
                        PositionBreakdown = positions,
                        CalculatedAtUtc = nowUtc
                    });
                }
            }

            var newChampionIds = newStats.Select(s => s.ChampionId).ToHashSet();
            foreach (var existing in existingStats)
            {
                if (!newChampionIds.Contains(existing.ChampionId))
                {
                    accountDb.MainChampionStats.Remove(existing);
                    totalStatsRemoved++;
                }
            }

            foreach (var stat in newStats)
            {
                if (statsByChampion.TryGetValue(stat.ChampionId, out var existing))
                {
                    existing.TotalMatches = stat.TotalMatches;
                    existing.ChampionMatches = stat.ChampionMatches;
                    existing.PlayRate = stat.PlayRate;
                    existing.IsMain = stat.IsMain;
                    existing.PrimaryPosition = stat.PrimaryPosition;
                    existing.PositionBreakdown = stat.PositionBreakdown;
                    existing.CalculatedAtUtc = stat.CalculatedAtUtc;
                }
                else
                {
                    accountDb.MainChampionStats.Add(stat);
                }

                totalStatsUpserted++;
            }

            var accountEntity = await accountDb.RiotAccounts
                .FirstOrDefaultAsync(a => a.PlatformId == account.PlatformId && a.Puuid == account.Puuid, ct);

            if (accountEntity is not null)
            {
                accountEntity.LastMainCalcAtUtc = nowUtc;
            }

            await accountDb.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            processed++;
        }

        logger.LogInformation(
            "Main analysis summary: accountsProcessed={Accounts}, statsUpserted={Upserted}, statsRemoved={Removed}.",
            processed,
            totalStatsUpserted,
            totalStatsRemoved);
    }

    private static bool IsValidTeamPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return false;
        }

        var normalized = position.Trim();
        return !normalized.Equals("NONE", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePosition(string position)
        => position.Trim().ToUpperInvariant();

    private sealed record AccountKey(string PlatformId, string Puuid);

    private sealed record ParticipantRow(int ChampionId, string TeamPosition);
}
