using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class MainAnalysisProcess(
    ILogger<MainAnalysisProcess> logger,
    IDataSessionFactory sessionFactory,
    ProcessRunRecorder runRecorder,
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

        var startedAt = DateTime.UtcNow;

        try
        {
            await using var session = await sessionFactory.CreateAsync(ct);

            var accounts = await session.RiotAccounts
                .GetAccountsForMainAnalysisAsync(cutoff, batchSize, ct);

            if (accounts.Count == 0)
            {
                logger.LogInformation("No accounts eligible for main analysis.");
                return;
            }

            var processed = 0;
            var totalStatsUpserted = 0;
            var totalStatsRemoved = 0;
            var demotedAccounts = 0;

            foreach (var account in accounts)
            {
                ct.ThrowIfCancellationRequested();

                await using var accountSession = await sessionFactory.CreateAsync(ct);
                await using var transaction = await accountSession.BeginTransactionAsync(ct);

                var participantRows = await accountSession.MatchParticipants
                    .GetRecentParticipantsAsync(account.PlatformId, account.Puuid, queueId, matchesToConsider, ct);

                var validParticipants = participantRows
                    .Where(p => IsValidTeamPosition(p.TeamPosition))
                    .Select(p => new ParticipantRow(p.ChampionId, NormalizePosition(p.TeamPosition)))
                    .ToList();

                var totalMatches = validParticipants.Count;

                var existingStats = await accountSession.MainChampionStats
                    .GetByAccountAsync(account.PlatformId, account.Puuid, ct);

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
                var newStatsByChampion = newStats.ToDictionary(s => s.ChampionId);
                foreach (var existing in existingStats)
                {
                    if (!newChampionIds.Contains(existing.ChampionId))
                    {
                        accountSession.MainChampionStats.Remove(existing);
                        totalStatsRemoved++;
                    }
                }

                var demoteToScored = existingStats
                    .Where(s => s.IsMain)
                    .Any(s => !newStatsByChampion.TryGetValue(s.ChampionId, out var stat)
                              || stat.PlayRate < options.CriticalPlayRateThreshold);

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
                        accountSession.MainChampionStats.Add(stat);
                    }

                    totalStatsUpserted++;
                }

                var accountEntity = await accountSession.RiotAccounts
                    .GetByKeyAsync(account.PlatformId, account.Puuid, ct);

                if (accountEntity is not null)
                {
                    accountEntity.LastMainCalcAtUtc = nowUtc;
                }

                if (demoteToScored)
                {
                    var updated = await accountSession.MainCandidates
                        .SetStatusForAccountAsync(account.PlatformId, account.Puuid, MainCandidateStatus.Validated, MainCandidateStatus.Scored, ct);

                    if (updated > 0)
                    {
                        demotedAccounts++;
                        logger.LogInformation(
                            "Demoted candidates for {Platform}/{Puuid} to Scored due to critical play rate.",
                            account.PlatformId,
                            account.Puuid);
                    }
                }

                await accountSession.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                processed++;
            }

            logger.LogInformation(
                "Main analysis summary: accountsProcessed={Accounts}, statsUpserted={Upserted}, statsRemoved={Removed}.",
                processed,
                totalStatsUpserted,
                totalStatsRemoved);

            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("MainAnalysis", startedAt, finishedAt, ProcessRunStatus.Success,
                new { accountsProcessed = processed, statsUpserted = totalStatsUpserted, statsRemoved = totalStatsRemoved, demotedAccounts },
                null, ct);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("MainAnalysis", startedAt, finishedAt, ProcessRunStatus.Failed, null, ex.Message, ct);
            throw;
        }
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

}
