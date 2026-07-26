using Core.Lol.Identifiers;
using Data.Entities;
using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Retires mains whose player stopped playing, and brings back the ones who returned (#900).
/// </summary>
/// <remarks>
/// Activity is read from champion-mastery-v4 (<c>lastPlayTime</c>): one call per account,
/// covering every champion at once. Match history would answer the same question at the cost
/// of a full match-v5 page per account — and the whole point is to stop spending that budget
/// on players who no longer play, so it can go to the mains who do.
///
/// The verdict is per champion, not per account: a player who dropped one champion but still
/// plays another keeps the second one active. Rows are flagged, never deleted — a returning
/// player is reactivated by the next pass instead of going through discovery again.
/// </remarks>
public sealed class MainActivityProcess(
    ILogger<MainActivityProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    TimeProvider timeProvider,
    IOptions<MainActivityOptions> activityOptions) : IIngestorProcess
{
    public string Name => "MainActivity";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var options = activityOptions.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var accounts = await LoadAccountsDueForCheckAsync(options, nowUtc, ct);
        if (accounts.Count == 0)
        {
            logger.LogInformation("No mains due for an activity check.");
            return new NoWorkSummary("No mains due for an activity check.", 0);
        }

        var summary = await CheckAccountsAsync(accounts, options, nowUtc, ct);

        logger.LogInformation(
            OpsEvents.MainActivityCycleCompleted,
            "Main activity summary: inactiveAfterDays={InactiveAfterDays}, checked={Checked}, deactivated={Deactivated}, reactivated={Reactivated}, failed={Failed}, skipped={Skipped}.",
            options.InactiveAfterDays,
            summary.Checked,
            summary.Deactivated,
            summary.Reactivated,
            summary.Failed,
            summary.Skipped);

        return new MainActivitySummary(
            summary.Checked,
            summary.Deactivated,
            summary.Reactivated,
            summary.Failed,
            summary.Skipped);
    }

    private async Task<IReadOnlyList<AccountKey>> LoadAccountsDueForCheckAsync(
        MainActivityOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var cutoff = options.RecheckAfterHours > 0
            ? nowUtc.AddHours(-options.RecheckAfterHours)
            : DateTime.MinValue;

        return await session.RiotAccounts
            .GetAccountsForActivityCheckAsync(cutoff, Math.Max(1, options.BatchSize), ct);
    }

    private async Task<ActivitySummary> CheckAccountsAsync(
        IReadOnlyList<AccountKey> accounts,
        MainActivityOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var summary = new ActivitySummary();
        await using var session = await sessionFactory.CreateAsync(ct);

        // Tracked on purpose: the IsActive flips and the LastActivityCheckAtUtc stamps
        // below are change-tracked mutations of these very entities, flushed once at the
        // end of the run.
        var statsByAccount = await session.MainChampionStats.GetByAccountsAsync(accounts, ct);
        var accountEntitiesByKey = await session.RiotAccounts.GetByKeysAsync(accounts, ct);
        var inactiveBefore = nowUtc.AddDays(-Math.Max(0, options.InactiveAfterDays));

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            if (!PlatformId.TryParse(account.PlatformId, out var platformId))
            {
                logger.LogWarning(
                    "Skipping activity check for {Puuid}: invalid platform {PlatformId}.",
                    account.Puuid,
                    account.PlatformId);
                summary.Skipped++;
                continue;
            }

            List<RiotChampionMasteryDto> masteries;
            try
            {
                masteries = await riotPlatformClient.GetChampionMasteriesAsync(platformId.Route, account.Puuid, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed mastery lookup is not evidence of inactivity: leave the account
                // untouched — including its LastActivityCheckAtUtc, so it stays at the head
                // of the selection — and retry it next run.
                logger.LogWarning(
                    ex,
                    "Failed mastery activity check for {Platform}/{Puuid}.",
                    account.PlatformId,
                    account.Puuid);
                summary.Failed++;
                continue;
            }

            var stats = statsByAccount.TryGetValue(account, out var accountStats) ? accountStats : [];
            ApplyVerdict(stats, masteries, inactiveBefore, summary);

            if (accountEntitiesByKey.TryGetValue(account, out var accountEntity))
            {
                accountEntity.LastActivityCheckAtUtc = nowUtc;
            }

            summary.Checked++;
        }

        await session.SaveChangesAsync(ct);
        return summary;
    }

    private static void ApplyVerdict(
        IReadOnlyCollection<MainChampionStat> stats,
        IReadOnlyCollection<RiotChampionMasteryDto> masteries,
        DateTime inactiveBefore,
        ActivitySummary summary)
    {
        var lastPlayByChampion = masteries
            .GroupBy(mastery => mastery.ChampionId)
            .ToDictionary(
                group => group.Key,
                group => DateTimeOffset.FromUnixTimeMilliseconds(group.Max(m => m.LastPlayTime)).UtcDateTime);

        foreach (var stat in stats)
        {
            if (!stat.IsMain)
            {
                continue;
            }

            // No mastery entry for a champion the player is a main on means Riot has no
            // record of them playing it at all — treat it as inactive rather than as a
            // reason to keep the row forever.
            var isActive = lastPlayByChampion.TryGetValue(stat.ChampionId, out var lastPlay)
                           && lastPlay >= inactiveBefore;

            if (isActive == stat.IsActive)
            {
                continue;
            }

            stat.IsActive = isActive;
            if (isActive)
            {
                summary.Reactivated++;
            }
            else
            {
                summary.Deactivated++;
            }
        }
    }

    private sealed class ActivitySummary
    {
        public int Checked { get; set; }
        public int Deactivated { get; set; }
        public int Reactivated { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }
}
