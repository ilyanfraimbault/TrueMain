using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.MainAnalysis;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MainAnalysisProcess(
    ILogger<MainAnalysisProcess> logger,
    IDataSessionFactory sessionFactory,
    IProcessRunRecorder runRecorder,
    IMainStatsCalculator mainStatsCalculator,
    IMainDemotionPolicy mainDemotionPolicy,
    IOptions<MainAnalysisOptions> analysisOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = analysisOptions.Value;
        var nowUtc = DateTime.UtcNow;
        var startedAt = DateTime.UtcNow;
        var cutoff = options.RecomputeAfterHours > 0
            ? nowUtc.AddHours(-options.RecomputeAfterHours)
            : DateTime.MinValue;

        try
        {
            await using var session = await sessionFactory.CreateAsync(ct);
            var accounts = await session.RiotAccounts
                .GetAccountsForMainAnalysisAsync(cutoff, Math.Max(1, options.BatchSize), ct);

            if (accounts.Count == 0)
            {
                logger.LogInformation("No accounts eligible for main analysis.");
                await RecordNoOpAsync(startedAt, "No accounts eligible for main analysis.", ct);
                return;
            }

            var summary = await ProcessAccountsAsync(accounts, options, nowUtc, ct);
            logger.LogInformation(
                "Main analysis summary: accountsProcessed={Accounts}, statsUpserted={Upserted}, statsRemoved={Removed}.",
                summary.Processed,
                summary.TotalStatsUpserted,
                summary.TotalStatsRemoved);

            await runRecorder.RecordAsync(
                "MainAnalysis",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Success,
                new
                {
                    accountsProcessed = summary.Processed,
                    statsUpserted = summary.TotalStatsUpserted,
                    statsRemoved = summary.TotalStatsRemoved,
                    demotedAccounts = summary.DemotedAccounts
                },
                null,
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordAsync(
                "MainAnalysis",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Failed,
                null,
                ex.Message,
                ct);
            throw;
        }
    }

    private async Task<AnalysisSummary> ProcessAccountsAsync(
        IReadOnlyList<AccountKey> accounts,
        MainAnalysisOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var summary = new AnalysisSummary();
        var processingBatchSize = Math.Max(1, options.ProcessingBatchSize);

        for (var i = 0; i < accounts.Count; i += processingBatchSize)
        {
            var batch = accounts.Skip(i).Take(processingBatchSize).ToList();
            await using var batchSession = await sessionFactory.CreateAsync(ct);
            await using var transaction = await batchSession.BeginTransactionAsync(ct);

            var existingStatsByAccount = await batchSession.MainChampionStats.GetByAccountsAsync(batch, ct);
            var accountEntitiesByKey = await batchSession.RiotAccounts.GetByKeysAsync(batch, ct);

            foreach (var account in batch)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessSingleAccountAsync(
                    batchSession,
                    account,
                    existingStatsByAccount,
                    accountEntitiesByKey,
                    options,
                    nowUtc,
                    summary,
                    ct);
            }

            await batchSession.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogDebug(
                "Processed batch {BatchStart}-{BatchEnd}/{Total} accounts.",
                i + 1,
                Math.Min(i + processingBatchSize, accounts.Count),
                accounts.Count);
        }

        return summary;
    }

    private async Task ProcessSingleAccountAsync(
        IDataSession session,
        AccountKey account,
        IReadOnlyDictionary<AccountKey, List<MainChampionStat>> existingStatsByAccount,
        IReadOnlyDictionary<AccountKey, RiotAccount> accountEntitiesByKey,
        MainAnalysisOptions options,
        DateTime nowUtc,
        AnalysisSummary summary,
        CancellationToken ct)
    {
        var participantRows = await session.MatchParticipants
            .GetRecentParticipantsAsync(account.PlatformId, account.Puuid, options.QueueId, Math.Max(1, options.MatchesToConsider), ct);

        var existingStats = existingStatsByAccount.TryGetValue(account, out var stats)
            ? stats
            : [];

        var newStats = mainStatsCalculator.Calculate(
            account.PlatformId,
            account.Puuid,
            participantRows,
            options,
            nowUtc);

        var newStatsByChampion = newStats.ToDictionary(stat => stat.ChampionId);
        summary.TotalStatsRemoved += RemoveMissingStats(session, existingStats, newStatsByChampion.Keys.ToHashSet());
        summary.TotalStatsUpserted += UpsertStats(session, existingStats, newStats);

        if (accountEntitiesByKey.TryGetValue(account, out var accountEntity))
        {
            accountEntity.LastMainCalcAtUtc = nowUtc;
        }

        var shouldDemote = mainDemotionPolicy.ShouldDemote(
            existingStats,
            newStatsByChampion,
            options.CriticalPlayRateThreshold);

        if (shouldDemote)
        {
            var updated = await session.MainCandidates
                .SetStatusForAccountAsync(account.PlatformId, account.Puuid, MainCandidateStatus.Validated, MainCandidateStatus.Scored, ct);

            if (updated > 0)
            {
                summary.DemotedAccounts++;
                logger.LogInformation(
                    "Demoted candidates for {Platform}/{Puuid} to Scored due to critical play rate.",
                    account.PlatformId,
                    account.Puuid);
            }
        }

        summary.Processed++;
    }

    private static int RemoveMissingStats(
        IDataSession session,
        IReadOnlyCollection<MainChampionStat> existingStats,
        IReadOnlySet<int> newChampionIds)
    {
        var removed = 0;
        foreach (var existing in existingStats)
        {
            if (newChampionIds.Contains(existing.ChampionId))
            {
                continue;
            }

            session.MainChampionStats.Remove(existing);
            removed++;
        }

        return removed;
    }

    private static int UpsertStats(
        IDataSession session,
        IReadOnlyCollection<MainChampionStat> existingStats,
        IReadOnlyCollection<MainChampionStat> newStats)
    {
        var existingByChampion = existingStats.ToDictionary(stat => stat.ChampionId);

        foreach (var stat in newStats)
        {
            if (existingByChampion.TryGetValue(stat.ChampionId, out var existing))
            {
                existing.TotalMatches = stat.TotalMatches;
                existing.ChampionMatches = stat.ChampionMatches;
                existing.PlayRate = stat.PlayRate;
                existing.IsMain = stat.IsMain;
                existing.PrimaryPosition = stat.PrimaryPosition;
                existing.PositionBreakdown = stat.PositionBreakdown;
                existing.CalculatedAtUtc = stat.CalculatedAtUtc;
                continue;
            }

            session.MainChampionStats.Add(stat);
        }

        return newStats.Count;
    }

    private async Task RecordNoOpAsync(DateTime startedAtUtc, string reason, CancellationToken ct)
    {
        await runRecorder.RecordAsync(
            "MainAnalysis",
            startedAtUtc,
            DateTime.UtcNow,
            ProcessRunStatus.Success,
            new { reason, selected = 0 },
            null,
            ct);
    }

    private sealed class AnalysisSummary
    {
        public int Processed { get; set; }
        public int TotalStatsUpserted { get; set; }
        public int TotalStatsRemoved { get; set; }
        public int DemotedAccounts { get; set; }
    }
}
