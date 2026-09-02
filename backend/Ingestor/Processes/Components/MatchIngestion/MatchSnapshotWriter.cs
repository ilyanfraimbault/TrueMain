using System.Collections.Concurrent;
using Core.Lol.Identifiers;
using Core.Options;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class MatchSnapshotWriter(
    IRiotMatchClient riotMatchClient,
    TimeProvider timeProvider,
    IOptions<MainAnalysisOptions> mainAnalysisOptions) : IMatchSnapshotWriter
{
    private readonly int _targetQueueId = (int)mainAnalysisOptions.Value.QueueId;

    public async Task<SnapshotIngestionPlan> PrepareAsync(
        IDataSession session,
        string platformId,
        string puuid,
        RegionalRoute region,
        int matchesPerAccount,
        int maxFetchConcurrency,
        CancellationToken ct)
    {
        var allMatchIds = (await riotMatchClient.GetMatchIdsAsync(puuid, region, matchesPerAccount, ct))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var trackedAccount = await session.RiotAccounts.GetByKeyAsync(platformId, puuid, ct);
        var scan = await ExistingMatchScanner.ScanAsync(session, allMatchIds, ct);

        // Fetch the account's fresh matches in parallel into a concurrent collection.
        // Downstream order is irrelevant (catalog keys are deduplicated, and each match
        // persists independently), so a ConcurrentBag avoids a pre-sized slot array whose
        // uninitialized entries would surface as a NullReferenceException if a future
        // caller ever swallowed a fetch exception. The per-routing-value rate limiter
        // (#1359) is what bounds the request rate — the resilience handler never did,
        // despite what this comment used to claim — so MaxDegreeOfParallelism only caps
        // how many of these may be in flight at once.
        var fetched = new ConcurrentBag<FetchedMatch>();
        await Parallel.ForEachAsync(
            scan.Fresh,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxFetchConcurrency),
                CancellationToken = ct
            },
            async (matchId, token) =>
            {
                fetched.Add(new FetchedMatch(matchId, await riotMatchClient.GetMatchAsync(matchId, region, token)));
            });

        // Only the tracked queue (ranked solo/duo) is stored. Riot's type=ranked id fetch
        // still returns other ranked queues (notably flex); drop them up front — before
        // building perk-catalog keys or persisting — so a discarded match neither upserts
        // catalog rows nor enters match_participants / the timeline tables. Keeping them
        // out of the plan also keeps them out of the new-match set the timeline pass
        // consumes, so no orphan timeline is fetched or written (#680).
        //
        // Ordered by id so the write phase is deterministic: the bag's enumeration order
        // is not, and a stable order keeps batch boundaries (and their savepoints)
        // reproducible across a retry of the same account.
        var targetMatches = fetched
            .Where(item => item.Dto.Info.QueueId == _targetQueueId)
            .OrderBy(item => item.MatchId, StringComparer.Ordinal)
            .ToList();

        // One lookup for every account referenced by the batch instead of one per match,
        // and it lands here rather than under the write transaction — the mapper only
        // reads the row ids to link participants.
        var participantAccounts = await session.RiotAccounts.GetByKeysAsync(
            targetMatches
                .SelectMany(item => item.Dto.Info.Participants)
                .Select(participant => new AccountKey(platformId, participant.Puuid))
                .Distinct()
                .ToArray(),
            ct);

        return new SnapshotIngestionPlan(
            allMatchIds,
            scan.Existing,
            targetMatches,
            participantAccounts,
            trackedAccount?.Id);
    }

    public async Task<SnapshotIngestionResult> WriteAsync(
        IDataSession session,
        SnapshotIngestionPlan plan,
        string platformId,
        string puuid,
        int saveBatchSize,
        CancellationToken ct)
    {
        if (plan.TrackedAccountId is { } trackedAccountId)
        {
            await MatchAccountBackfiller.BackfillAsync(session, plan.ExistingMatchIds, puuid, trackedAccountId, ct);
        }

        var batchSize = Math.Max(1, saveBatchSize);
        var inserted = 0;
        var persistedIds = new List<string>(plan.TargetMatches.Count);

        for (var i = 0; i < plan.TargetMatches.Count; i += batchSize)
        {
            var batch = plan.TargetMatches.Skip(i).Take(batchSize).ToList();

            // Pre-resolve perk catalog ids for the whole batch BEFORE we add any
            // match/participant entities to the change tracker. The catalog upsert
            // performs its own SaveChanges; if it ran while match entities were already
            // Added, a catalog uniqueness clash would roll back the match transaction and
            // the subsequent ChangeTracker.Clear() would silently drop those entities,
            // leaving us free to commit orphan perk_selections in the batch's final
            // SaveChanges.
            var catalogKeys = batch
                .SelectMany(item => RiotMatchMapper.BuildPerkSelectionRows(item.Dto, item.MatchId))
                .Select(selection => selection.Key)
                .ToArray();
            var catalogIds = await session.MatchParticipants.GetOrCreatePerkCatalogIdsAsync(catalogKeys, ct);

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var (matchId, dto) in batch)
            {
                PersistMatch(session, dto, platformId, plan.ParticipantAccounts, catalogIds, nowUtc);
                persistedIds.Add(matchId);
                inserted++;
            }

            await session.SaveChangesAsync(ct);
        }

        return new SnapshotIngestionResult(plan.AllMatchIds, persistedIds, inserted, plan.ExistingMatchIds.Count);
    }

    private static void PersistMatch(
        IDataSession session,
        RiotMatchDto matchDto,
        string platformId,
        IReadOnlyDictionary<AccountKey, RiotAccount> participantAccounts,
        IReadOnlyDictionary<PerkCatalogKey, int> catalogIds,
        DateTime nowUtc)
    {
        var mapped = RiotMatchMapper.Map(matchDto, platformId, participantAccounts, nowUtc);

        session.Matches.Add(mapped.Match);
        session.MatchParticipants.AddRange(mapped.Participants);
        session.MatchBans.AddRange(mapped.Bans);

        var perkSelections = mapped.PerkSelections.Select(selection => new ParticipantPerkSelection
        {
            MatchId = selection.MatchId,
            ParticipantId = selection.ParticipantId,
            PerkSelectionCatalogId = catalogIds[selection.Key]
        });

        session.MatchParticipants.AddPerkSelections(perkSelections);
    }
}
