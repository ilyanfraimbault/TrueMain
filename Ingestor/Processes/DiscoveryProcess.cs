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
    private const string ProcessName = "Discovery";

    public async Task RunAsync(CancellationToken ct)
    {
        var options = discoveryOptions.Value;
        var startedAt = DateTime.UtcNow;

        if (!HasConfiguredPlatforms(options))
        {
            logger.LogWarning("No platforms configured (Discovery:Platforms).");
            await runRecorder.RecordNoOpAsync(
                ProcessName,
                startedAt,
                new { reason = "No platforms configured.", selected = 0 },
                ct);
            return;
        }

        try
        {
            var summaries = await DiscoverAcrossPlatformsAsync(options, ct);
            await runRecorder.RecordSuccessAsync(ProcessName, startedAt, BuildSuccessPayload(summaries), ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordFailureAsync(ProcessName, startedAt, ex, ct);
            throw;
        }
    }

    private static bool HasConfiguredPlatforms(DiscoveryOptions options)
    {
        return options.Platforms.Count > 0;
    }

    private async Task<List<PlatformSummary>> DiscoverAcrossPlatformsAsync(
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var summaries = new List<PlatformSummary>();

        foreach (var platformString in GetConfiguredPlatforms(options))
        {
            ct.ThrowIfCancellationRequested();
            if (!RiotDataHelpers.TryParsePlatform(platformString, out var platform))
            {
                logger.LogWarning("Skipping unknown platform '{Platform}'.", platformString);
                continue;
            }

            var platformSummary = await ProcessPlatformAsync(platform, options, ct);
            summaries.Add(platformSummary);
            LogPlatformSummary(platformSummary);
        }

        return summaries;
    }

    private static IEnumerable<string> GetConfiguredPlatforms(DiscoveryOptions options)
    {
        return options.Platforms.Distinct(StringComparer.OrdinalIgnoreCase);
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

    private void LogPlatformSummary(PlatformSummary platformSummary)
    {
        logger.LogInformation(
            "Discovery summary for {Platform}: accounts={AccountsProcessed}, newAccounts={NewAccounts}, candidatesInserted={Inserted}, candidatesUpdated={Updated}.",
            platformSummary.PlatformId,
            platformSummary.AccountsProcessed,
            platformSummary.NewAccountsDiscovered,
            platformSummary.CandidatesInserted,
            platformSummary.CandidatesUpdated);
    }

    private static object BuildSuccessPayload(IEnumerable<PlatformSummary> summaries)
    {
        return new
        {
            platforms = summaries.Select(summary => new
            {
                platform = summary.PlatformId,
                accountsProcessed = summary.AccountsProcessed,
                newAccounts = summary.NewAccountsDiscovered,
                candidatesInserted = summary.CandidatesInserted,
                candidatesUpdated = summary.CandidatesUpdated
            })
        };
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
