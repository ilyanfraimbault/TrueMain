using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class DiscoveryProcess(
    ILogger<DiscoveryProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    ILadderDiscoveryService ladderDiscoveryService,
    IAccountUpsertService accountUpsertService,
    ICandidateUpsertService candidateUpsertService,
    IRankSnapshotWriter rankSnapshotWriter,
    IOptions<DiscoveryOptions> discoveryOptions) : IIngestorProcess
{
    public string Name => "Discovery";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var options = discoveryOptions.Value;
        var platforms = NormalizePlatforms(options);

        if (platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (Discovery:Platforms).");
            return new { reason = "No platforms configured.", selected = 0 };
        }

        var summaries = await DiscoverAcrossPlatformsAsync(platforms, options, ct);
        return BuildSuccessPayload(summaries);
    }

    private static List<string> NormalizePlatforms(DiscoveryOptions options)
    {
        return options.Platforms
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Select(platform => platform.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<PlatformSummary>> DiscoverAcrossPlatformsAsync(
        IReadOnlyCollection<string> platforms,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var summaries = new List<PlatformSummary>();

        foreach (var platformString in platforms)
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

    private async Task<PlatformSummary> ProcessPlatformAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var platformId = platform.ToString();
        var summary = new PlatformSummary(platformId);
        var discovered = await ladderDiscoveryService.DiscoverSummonersAsync(platform, options, ct);

        if (discovered.Count == 0)
        {
            logger.LogInformation("No ladder entries for platform {Platform}.", platformId);
            return summary;
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        var saveBatchSize = Math.Max(1, options.SaveBatchSize);
        var newAccountsTarget = Math.Max(0, options.NewAccountsTarget);

        var latestByAccountId = await PreloadLatestSnapshotsAsync(session, platformId, discovered, ct);

        var pendingChanges = 0;
        var discoveredAccounts = 0;

        foreach (var item in discovered)
        {
            ct.ThrowIfCancellationRequested();

            var nowUtc = DateTime.UtcNow;
            var upsertResult = await accountUpsertService.UpsertAsync(session, platform, item.Summoner, nowUtc, ct);
            if (upsertResult.IsNew)
            {
                discoveredAccounts++;
                summary.NewAccountsDiscovered++;
            }

            if (item.Rank is not null)
            {
                latestByAccountId.TryGetValue(upsertResult.Account.Id, out var latest);
                var outcome = rankSnapshotWriter.Write(session, upsertResult.Account, item.Rank, latest, nowUtc);
                if (outcome == RankSnapshotOutcome.Inserted)
                {
                    summary.RankSnapshotsInserted++;
                }
                else
                {
                    summary.RankSnapshotsUnchanged++;
                }
            }

            var masteries = await riotPlatformClient.GetChampionMasteriesAsync(platform, item.Summoner.Puuid, ct);
            var candidateResult = await candidateUpsertService.UpsertAsync(
                session,
                platformId,
                item.Summoner.Puuid,
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

    private static async Task<Dictionary<Guid, RankSnapshot>> PreloadLatestSnapshotsAsync(
        IDataSession session,
        string platformId,
        IReadOnlyCollection<DiscoveredSummoner> discovered,
        CancellationToken ct)
    {
        var keys = discovered
            .Where(item => !string.IsNullOrWhiteSpace(item.Summoner.Puuid))
            .Select(item => new AccountKey(platformId, item.Summoner.Puuid))
            .Distinct()
            .ToList();

        if (keys.Count == 0)
        {
            return new Dictionary<Guid, RankSnapshot>();
        }

        var existing = await session.RiotAccounts.GetByKeysAsync(keys, ct);
        if (existing.Count == 0)
        {
            return new Dictionary<Guid, RankSnapshot>();
        }

        var existingIds = existing.Values.Select(account => account.Id).ToList();
        return await session.RankSnapshots.GetLatestForAccountsAsync(existingIds, ct);
    }

    private void LogPlatformSummary(PlatformSummary platformSummary)
    {
        logger.LogInformation(
            "Discovery summary for {Platform}: accounts={AccountsProcessed}, newAccounts={NewAccounts}, candidatesInserted={Inserted}, candidatesUpdated={Updated}, rankSnapshotsInserted={RankInserted}, rankSnapshotsUnchanged={RankUnchanged}.",
            platformSummary.PlatformId,
            platformSummary.AccountsProcessed,
            platformSummary.NewAccountsDiscovered,
            platformSummary.CandidatesInserted,
            platformSummary.CandidatesUpdated,
            platformSummary.RankSnapshotsInserted,
            platformSummary.RankSnapshotsUnchanged);
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
                candidatesUpdated = summary.CandidatesUpdated,
                rankSnapshotsInserted = summary.RankSnapshotsInserted,
                rankSnapshotsUnchanged = summary.RankSnapshotsUnchanged
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
        public int RankSnapshotsInserted { get; set; }
        public int RankSnapshotsUnchanged { get; set; }
    }
}
