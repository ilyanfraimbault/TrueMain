using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MainAnalysis;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MainAnalysisProcess(
    ILogger<MainAnalysisProcess> logger,
    IDataSessionFactory sessionFactory,
    IMainStatsCalculator mainStatsCalculator,
    IMainDemotionPolicy mainDemotionPolicy,
    IChampionCoverageProvider coverageProvider,
    TimeProvider timeProvider,
    IOptions<MainAnalysisOptions> analysisOptions) : IIngestorProcess
{
    public string Name => "MainAnalysis";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var options = analysisOptions.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var accounts = await LoadEligibleAccountsAsync(options, nowUtc, ct);
        if (accounts.Count == 0)
        {
            logger.LogInformation("No accounts eligible for main analysis.");
            return new NoWorkSummary("No accounts eligible for main analysis.", 0);
        }

        var coverage = await LoadCoverageAsync(ct);
        var summary = await AnalyzeAccountsInBatchesAsync(accounts, options, coverage, nowUtc, ct);
        logger.LogInformation(
            "Main analysis summary: accountsProcessed={Accounts}, statsUpserted={Upserted}, statsRemoved={Removed}.",
            summary.Processed,
            summary.TotalStatsUpserted,
            summary.TotalStatsRemoved);

        return BuildSuccessPayload(summary);
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

    // Coverage is loaded once in its own short-lived session before any batch work, freezing
    // the snapshot for the whole cycle while each batch opens its own session (AnalyzeBatchAsync).
    // ScoringProcess reuses a single session for coverage + scoring because both fit one short
    // transaction; the per-batch lifecycle here makes sharing one session impractical. Don't
    // "simplify" this into the per-batch sessions.
    private async Task<ChampionCoverageSnapshot> LoadCoverageAsync(CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        return await coverageProvider.GetSnapshotAsync(session, ct);
    }

    private async Task<AnalysisSummary> AnalyzeAccountsInBatchesAsync(
        IReadOnlyList<AccountKey> accounts,
        MainAnalysisOptions options,
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var summary = new AnalysisSummary();
        var processingBatchSize = Math.Max(1, options.ProcessingBatchSize);

        for (var i = 0; i < accounts.Count; i += processingBatchSize)
        {
            var batch = accounts.Skip(i).Take(processingBatchSize).ToList();
            var batchResult = await AnalyzeBatchAsync(batch, options, coverage, nowUtc, ct);
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
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var summary = new AnalysisSummary();
        await using var session = await sessionFactory.CreateAsync(ct);

        // Reads stay outside the transaction (#264): they only feed an in-memory
        // computation and take no row locks, so running them under BEGIN extended
        // the lock lifetime for nothing. The stats and accounts must stay TRACKED —
        // the writes below are change-tracked mutations of these very entities
        // (in-place stat updates, Remove, LastMainCalcAtUtc stamping) — so no
        // AsNoTracking here; the participant rows are already an untracked raw-SQL
        // projection, capped at MatchesToConsider rows per account.
        var existingStatsByAccount = await session.MainChampionStats.GetByAccountsAsync(batch, ct);
        var accountEntitiesByKey = await session.RiotAccounts.GetByKeysAsync(batch, ct);
        var participantRowsByAccount = await session.MatchParticipants
            .GetRecentParticipantsByAccountsAsync(batch, (int)options.QueueId, Math.Max(1, options.MatchesToConsider), ct);

        var accountsToDemote = new List<AccountKey>();

        foreach (var account in batch)
        {
            ct.ThrowIfCancellationRequested();
            var accountResult = AnalyzeSingleAccount(
                session,
                account,
                participantRowsByAccount,
                existingStatsByAccount,
                accountEntitiesByKey,
                options,
                coverage,
                nowUtc,
                accountsToDemote);
            summary.Merge(accountResult);
        }

        // Deliberate write boundary (#264): the stat delta, the LastMainCalcAtUtc
        // stamps and the candidate demotions commit — or roll back — as one unit.
        // Pinned by MainAnalysisProcessIntegrationTests
        // .RunAsync_ShouldRollBackStatWrites_WhenDemotionFails.
        await using var transaction = await session.BeginTransactionAsync(ct);
        await session.SaveChangesAsync(ct);
        summary.DemotedAccounts += await DemoteCandidatesAsync(session, accountsToDemote, ct);
        await transaction.CommitAsync(ct);
        return summary;
    }

    private AnalysisSummary AnalyzeSingleAccount(
        IDataSession session,
        AccountKey account,
        IReadOnlyDictionary<AccountKey, List<ParticipantRow>> participantRowsByAccount,
        IReadOnlyDictionary<AccountKey, List<MainChampionStat>> existingStatsByAccount,
        IReadOnlyDictionary<AccountKey, RiotAccount> accountEntitiesByKey,
        MainAnalysisOptions options,
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        ICollection<AccountKey> accountsToDemote)
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
            coverage,
            nowUtc);

        // Every stat the calculator emits carries the account's total valid
        // sample size; 0 rows means no classifiable games this cycle.
        var newTotalMatches = newStats.Count > 0 ? newStats[0].TotalMatches : 0;
        var hasEstablishedMain = existingStats.Any(stat => stat.IsMain);

        // Thin-sample guard (#825): an established main that became eligible via
        // the passive-harvest path can arrive with a recent sample too small to
        // classify anyone as a main (< MinMatchesToEvaluate). Applying the delta
        // then would delete the existing main (RemoveMissingChampionStats) and
        // replace it with non-main rows, dropping the player off the leaderboard
        // on a sample we explicitly deem insufficient. Leave the established main
        // untouched instead, but still stamp LastMainCalcAtUtc so the account
        // waits a full recompute cycle before we retry — by then more games may
        // have been harvested. Accounts with no established main keep the prior
        // behaviour (nothing to protect).
        if (hasEstablishedMain && newTotalMatches < options.MinMatchesToEvaluate)
        {
            TouchAccountLastMainCalc(account, accountEntitiesByKey, nowUtc);
            summary.Processed++;
            return summary;
        }

        var newStatsByChampion = newStats.ToDictionary(stat => stat.ChampionId);
        ApplyStatsDelta(session, existingStats, newStats, summary);
        TouchAccountLastMainCalc(account, accountEntitiesByKey, nowUtc);

        var shouldDemote = mainDemotionPolicy.ShouldDemote(
            existingStats,
            newStatsByChampion,
            options.CriticalPlayRateThreshold);

        if (shouldDemote)
        {
            accountsToDemote.Add(account);
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

    private async Task<int> DemoteCandidatesAsync(
        IDataSession session,
        IReadOnlyCollection<AccountKey> accountsToDemote,
        CancellationToken ct)
    {
        var demoted = 0;
        foreach (var account in accountsToDemote)
        {
            var updated = await session.MainCandidates
                .SetStatusForAccountAsync(account.PlatformId, account.Puuid, MainCandidateStatus.Validated, MainCandidateStatus.Scored, ct);

            if (updated > 0)
            {
                demoted++;
                logger.LogInformation(
                    "Demoted candidates for {Platform}/{Puuid} to Scored due to critical play rate.",
                    account.PlatformId,
                    account.Puuid);
            }
        }

        return demoted;
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
                existing.IsExtendedSample = stat.IsExtendedSample;
                existing.PrimaryPosition = stat.PrimaryPosition;
                existing.PositionBreakdown = stat.PositionBreakdown;
                existing.CalculatedAtUtc = stat.CalculatedAtUtc;
                continue;
            }

            session.MainChampionStats.Add(stat);
        }

        return newStats.Count;
    }

    private static MainAnalysisSummary BuildSuccessPayload(AnalysisSummary summary)
    {
        return new MainAnalysisSummary(
            summary.Processed,
            summary.TotalStatsUpserted,
            summary.TotalStatsRemoved,
            summary.DemotedAccounts);
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
