using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class DiscoveryProcess(
    ILogger<DiscoveryProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    IProcessRunRecorder runRecorder,
    ILadderDiscoveryService ladderDiscoveryService,
    IAccountUpsertService accountUpsertService,
    ICandidateUpsertService candidateUpsertService,
    IOptions<DiscoveryOptions> discoveryOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = discoveryOptions.Value;
        var startedAt = DateTime.UtcNow;
        var summaries = new List<PlatformSummary>();

        if (options.Platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (Discovery:Platforms).");
            await RecordNoOpAsync(startedAt, "No platforms configured.", ct);
            return;
        }

        try
        {
            foreach (var platformString in options.Platforms.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();

                if (!RiotDataHelpers.TryParsePlatform(platformString, out var platform))
                {
                    logger.LogWarning("Skipping unknown platform '{Platform}'.", platformString);
                    continue;
                }

                var platformSummary = await ProcessPlatformAsync(platform, options, ct);
                summaries.Add(platformSummary);

                logger.LogInformation(
                    "Discovery summary for {Platform}: accounts={AccountsProcessed}, newAccounts={NewAccounts}, candidatesInserted={Inserted}, candidatesUpdated={Updated}.",
                    platformSummary.PlatformId,
                    platformSummary.AccountsProcessed,
                    platformSummary.NewAccountsDiscovered,
                    platformSummary.CandidatesInserted,
                    platformSummary.CandidatesUpdated);
            }

            await runRecorder.RecordAsync(
                "Discovery",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Success,
                new
                {
                    platforms = summaries.Select(summary => new
                    {
                        platform = summary.PlatformId,
                        accountsProcessed = summary.AccountsProcessed,
                        newAccounts = summary.NewAccountsDiscovered,
                        candidatesInserted = summary.CandidatesInserted,
                        candidatesUpdated = summary.CandidatesUpdated
                    })
                },
                null,
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordAsync("Discovery", startedAt, DateTime.UtcNow, ProcessRunStatus.Failed, null, ex.Message, ct);
            throw;
        }
    }

    private async Task<PlatformSummary> ProcessPlatformAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var platformId = platform.ToString();
        var summary = new PlatformSummary(platformId);
        var summoners = await ladderDiscoveryService.DiscoverSummonersAsync(platform, options, ct);

        if (summoners.Count == 0)
        {
            logger.LogInformation("No ladder entries for platform {Platform}.", platformId);
            return summary;
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        var saveBatchSize = Math.Max(1, options.SaveBatchSize);
        var newAccountsTarget = Math.Max(0, options.NewAccountsTarget);

        var pendingChanges = 0;
        var discoveredAccounts = 0;

        foreach (var summoner in summoners)
        {
            ct.ThrowIfCancellationRequested();

            var nowUtc = DateTime.UtcNow;
            if (await accountUpsertService.UpsertAsync(session, platform, summoner, nowUtc, ct))
            {
                discoveredAccounts++;
                summary.NewAccountsDiscovered++;
            }

            var masteries = await riotPlatformClient.GetChampionMasteriesAsync(platform, summoner.Puuid, ct);
            var candidateResult = await candidateUpsertService.UpsertAsync(
                session,
                platformId,
                summoner.Puuid,
                masteries,
                options,
                nowUtc,
                ct);

            summary.AccountsProcessed++;
            summary.CandidatesInserted += candidateResult.Inserted;
            summary.CandidatesUpdated += candidateResult.Updated;

            pendingChanges++;
            if (pendingChanges >= saveBatchSize)
            {
                await session.SaveChangesAsync(ct);
                pendingChanges = 0;
            }

            if (newAccountsTarget > 0 && discoveredAccounts >= newAccountsTarget)
            {
                logger.LogInformation(
                    "Discovery reached new accounts target ({Target}) for platform {Platform}. Stopping early.",
                    newAccountsTarget,
                    platformId);
                break;
            }
        }

        if (pendingChanges > 0)
        {
            await session.SaveChangesAsync(ct);
        }

        return summary;
    }

    private async Task RecordNoOpAsync(DateTime startedAtUtc, string reason, CancellationToken ct)
    {
        await runRecorder.RecordAsync(
            "Discovery",
            startedAtUtc,
            DateTime.UtcNow,
            ProcessRunStatus.Success,
            new { reason, selected = 0 },
            null,
            ct);
    }

    private sealed class PlatformSummary(string platformId)
    {
        public string PlatformId { get; } = platformId;
        public int AccountsProcessed { get; set; }
        public int NewAccountsDiscovered { get; set; }
        public int CandidatesInserted { get; set; }
        public int CandidatesUpdated { get; set; }
    }
}
