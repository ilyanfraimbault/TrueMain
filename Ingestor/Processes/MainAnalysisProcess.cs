using Core.Options;
using Data.Entities;
using Data.Repositories;
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
    private const string ProcessName = "MainAnalysis";

    public async Task RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            var options = analysisOptions.Value;
            var nowUtc = DateTime.UtcNow;
            var accounts = await LoadEligibleAccountsAsync(options, nowUtc, ct);
            if (accounts.Count == 0)
            {
                logger.LogInformation("No accounts eligible for main analysis.");
                await runRecorder.RecordNoOpAsync(
                    ProcessName,
                    startedAt,
                    new { reason = "No accounts eligible for main analysis.", selected = 0 },
                    ct);
                return;
            }

            var summary = await AnalyzeAccountsInBatchesAsync(accounts, options, nowUtc, ct);
            logger.LogInformation(
                "Main analysis summary: accountsProcessed={Accounts}, statsUpserted={Upserted}, statsRemoved={Removed}.",
                summary.Processed,
                summary.TotalStatsUpserted,
                summary.TotalStatsRemoved);

            await runRecorder.RecordSuccessAsync(ProcessName, startedAt, BuildSuccessPayload(summary), ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordFailureAsync(ProcessName, startedAt, ex, ct);
            throw;
        }
    }

    private async Task<IReadOnlyList<AccountKey>> LoadEligibleAccountsAsync(
        MainAnalysisOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var cutoff = options.RecomputeAfterHours > 0
            ? nowUtc.AddHours(-options.RecomputeAfterHours)
            : DateTime.MinValue;

        return await session.RiotAccounts
            .GetAccountsForMainAnalysisAsync(cutoff, Math.Max(1, options.BatchSize), ct);
    }

    private async Task<AnalysisSummary> AnalyzeAccountsInBatchesAsync(
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
            var batchResult = await AnalyzeBatchAsync(batch, options, nowUtc, ct);
            summary.Merge(batchResult);

            logger.LogDebug(
                "Processed batch {BatchStart}-{BatchEnd}/{Total} accounts.",
                i + 1,
                Math.Min(i + processingBatchSize, accounts.Count),
                accounts.Count);
        }

        return summary;
    }

    private async Task<AnalysisSummary> AnalyzeBatchAsync(
        IReadOnlyList<AccountKey> batch,
        MainAnalysisOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var summary = new AnalysisSummary();
        await using var session = await sessionFactory.CreateAsync(ct);
        await using var transaction = await session.BeginTransactionAsync(ct);
        var existingStatsByAccount = await session.MainChampionStats.GetByAccountsAsync(batch, ct);
        var accountEntitiesByKey = await session.RiotAccounts.GetByKeysAsync(batch, ct);
        var participantRowsByAccount = await session.MatchParticipants
            .GetRecentParticipantsByAccountsAsync(batch, options.QueueId, Math.Max(1, options.MatchesToConsider), ct);

        foreach (var account in batch)
        {
            ct.ThrowIfCancellationRequested();
            var accountResult = await AnalyzeSingleAccountAsync(
                session,
                account,
                participantRowsByAccount,
                existingStatsByAccount,
                accountEntitiesByKey,
                options,
                nowUtc,
                ct);
            summary.Merge(accountResult);
        }

        await session.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return summary;
    }

    private async Task<AnalysisSummary> AnalyzeSingleAccountAsync(
        IDataSession session,
        AccountKey account,
        IReadOnlyDictionary<AccountKey, List<ParticipantRow>> participantRowsByAccount,
        IReadOnlyDictionary<AccountKey, List<MainChampionStat>> existingStatsByAccount,
        IReadOnlyDictionary<AccountKey, RiotAccount> accountEntitiesByKey,
        MainAnalysisOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var summary = new AnalysisSummary();
        var participantRows = participantRowsByAccount.TryGetValue(account, out var rows)
            ? rows
            : [];

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
        ApplyStatsDelta(session, existingStats, newStats, summary);
        TouchAccountLastMainCalc(account, accountEntitiesByKey, nowUtc);

        var shouldDemote = mainDemotionPolicy.ShouldDemote(
            existingStats,
            newStatsByChampion,
            options.CriticalPlayRateThreshold);

        if (shouldDemote)
        {
            await TryDemoteCandidateAsync(session, account, summary, ct);
        }

        summary.Processed++;
        return summary;
    }

    private static void ApplyStatsDelta(
        IDataSession session,
        IReadOnlyCollection<MainChampionStat> existingStats,
        IReadOnlyCollection<MainChampionStat> newStats,
        AnalysisSummary summary)
    {
        var newStatsByChampionIds = newStats.Select(stat => stat.ChampionId).ToHashSet();
        summary.TotalStatsRemoved += RemoveMissingChampionStats(session, existingStats, newStatsByChampionIds);
        summary.TotalStatsUpserted += UpsertChampionStats(session, existingStats, newStats);
    }

    private static void TouchAccountLastMainCalc(
        AccountKey account,
        IReadOnlyDictionary<AccountKey, RiotAccount> accountEntitiesByKey,
        DateTime nowUtc)
    {
        if (accountEntitiesByKey.TryGetValue(account, out var accountEntity))
        {
            accountEntity.LastMainCalcAtUtc = nowUtc;
        }
    }

    private async Task TryDemoteCandidateAsync(
        IDataSession session,
        AccountKey account,
        AnalysisSummary summary,
        CancellationToken ct)
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

    private static int RemoveMissingChampionStats(
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

    private static int UpsertChampionStats(
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
                existing.IsOtp = stat.IsOtp;
                existing.PrimaryPosition = stat.PrimaryPosition;
                existing.PositionBreakdown = stat.PositionBreakdown;
                existing.CalculatedAtUtc = stat.CalculatedAtUtc;
                continue;
            }

            session.MainChampionStats.Add(stat);
        }

        return newStats.Count;
    }

    private static object BuildSuccessPayload(AnalysisSummary summary)
    {
        return new
        {
            accountsProcessed = summary.Processed,
            statsUpserted = summary.TotalStatsUpserted,
            statsRemoved = summary.TotalStatsRemoved,
            demotedAccounts = summary.DemotedAccounts
        };
    }

    private sealed class AnalysisSummary
    {
        public int Processed { get; set; }
        public int TotalStatsUpserted { get; set; }
        public int TotalStatsRemoved { get; set; }
        public int DemotedAccounts { get; set; }

        public void Merge(AnalysisSummary other)
        {
            Processed += other.Processed;
            TotalStatsUpserted += other.TotalStatsUpserted;
            TotalStatsRemoved += other.TotalStatsRemoved;
            DemotedAccounts += other.DemotedAccounts;
        }
    }
}
