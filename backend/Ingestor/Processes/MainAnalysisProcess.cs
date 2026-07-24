using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MainAnalysis;
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

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var options = analysisOptions.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var accounts = await LoadEligibleAccountsAsync(options, nowUtc, ct);
        if (accounts.Count == 0)
        {
            logger.LogInformation("No accounts eligible for main analysis.");
            return new { reason = "No accounts eligible for main analysis.", selected = 0 };
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

        // The three reads stay OUTSIDE the transaction (#264). They only feed an
        // in-memory computation, and running them under BEGIN held their locks —
        // and, behind pgbouncer's transaction pooling, a server connection — for the
        // whole batch for nothing. Nothing here depends on read-then-write
        // atomicity: on the default Read Committed level every statement takes its
        // own snapshot, so grouping these SELECTs with the writes never gave the
        // batch a consistent read snapshot either, and no read is repeated after a
        // write. The read-to-write window is unchanged — the reads already happened
        // before the mutation loop — so the xmin optimistic-concurrency guard on
        // riot_accounts is exposed exactly as before.
        //
        // The stats and accounts come back tracked on purpose: the batch's writes
        // are change-tracked mutations of those very entities (in-place stat
        // updates, Remove, LastMainCalcAtUtc stamping), so AsNoTracking would break
        // them (and those repository methods are shared with other processes). The
        // participant rows are already untracked — a projection over raw SQL,
        // chunked per platform and capped at MatchesToConsider rows per account, so
        // nothing extra is pulled into memory here.
        var existingStatsByAccount = await session.MainChampionStats.GetByAccountsAsync(batch, ct);
        var accountEntitiesByKey = await session.RiotAccounts.GetByKeysAsync(batch, ct);
        var participantRowsByAccount = await session.MatchParticipants
            .GetRecentParticipantsByAccountsAsync(batch, (int)options.QueueId, Math.Max(1, options.MatchesToConsider), ct);

        // At most one key per account in the batch, so this cannot grow with the
        // dataset.
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

        // Only the writes are transacted, keeping the per-batch write boundary: the
        // stat delta, the LastMainCalcAtUtc stamps and the candidate demotions still
        // commit — or roll back — as one unit. The demotions moved after
        // SaveChangesAsync instead of running interleaved in the loop; they are
        // predicate-filtered ExecuteUpdates on another table (main_candidates) that
        // nothing in this batch reads back, so the outcome is identical while the
        // rows they lock are held for a shorter time.
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
