using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class MatchSnapshotWriter(IRiotMatchClient riotMatchClient) : IMatchSnapshotWriter
{
    public async Task<SnapshotIngestionResult> IngestSnapshotsAsync(
        IDataSession session,
        string platformId,
        string puuid,
        RegionalRoute region,
        int matchesPerAccount,
        int saveBatchSize,
        int maxFetchConcurrency,
        CancellationToken ct)
    {
        var allMatchIds = (await riotMatchClient.GetMatchIdsAsync(puuid, region, matchesPerAccount, ct))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var trackedAccount = await session.RiotAccounts.GetByKeyAsync(platformId, puuid, ct);
        var scan = await ExistingMatchScanner.ScanAsync(session, allMatchIds, ct);
        var batchSize = Math.Max(1, saveBatchSize);
        var fetchConcurrency = Math.Max(1, maxFetchConcurrency);

        if (trackedAccount is not null)
        {
            await MatchAccountBackfiller.BackfillAsync(session, scan.Existing, puuid, trackedAccount.Id, batchSize, ct);
        }

        var inserted = 0;
        for (var i = 0; i < scan.Fresh.Count; i += batchSize)
        {
            var batchIds = scan.Fresh.Skip(i).Take(batchSize).ToList();

            // Fetch the batch's matches in parallel. Each result is written to
            // its own slot so the array stays ordered and the concurrent writes
            // never collide. The resilience handler enforces per-region rate
            // limiting, so MaxDegreeOfParallelism only caps the in-flight count.
            var fetchedSlots = new (string Id, RiotMatchDto Dto)[batchIds.Count];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, batchIds.Count),
                new ParallelOptions { MaxDegreeOfParallelism = fetchConcurrency, CancellationToken = ct },
                async (index, token) =>
                {
                    var matchId = batchIds[index];
                    fetchedSlots[index] = (matchId, await riotMatchClient.GetMatchAsync(matchId, region, token));
                });

            var fetched = fetchedSlots.ToList();

            // Pre-resolve perk catalog ids for the whole batch BEFORE we add
            // any match/participant entities to the change tracker. The
            // catalog upsert performs its own SaveChanges; if it ran while
            // match entities were already Added, a catalog uniqueness clash
            // would roll back the match transaction and the subsequent
            // ChangeTracker.Clear() would silently drop those entities,
            // leaving us free to commit orphan perk_selections in the
            // batch's final SaveChanges.
            var catalogKeys = fetched
                .SelectMany(item => RiotMatchMapper.BuildPerkSelectionRows(item.Dto, item.Id))
                .Select(selection => selection.Key)
                .ToArray();
            var catalogIds = await session.MatchParticipants.GetOrCreatePerkCatalogIdsAsync(catalogKeys, ct);

            foreach (var (_, dto) in fetched)
            {
                await PersistMatchAsync(session, dto, platformId, catalogIds, ct);
                inserted++;
            }

            await session.SaveChangesAsync(ct);
        }

        return new SnapshotIngestionResult(allMatchIds, scan.Fresh, inserted, allMatchIds.Count - scan.Fresh.Count);
    }

    private static async Task PersistMatchAsync(
        IDataSession session,
        RiotMatchDto matchDto,
        string platformId,
        IReadOnlyDictionary<PerkCatalogKey, int> catalogIds,
        CancellationToken ct)
    {
        var participantAccounts = await session.RiotAccounts.GetByKeysAsync(
            matchDto.Info.Participants
                .Select(participant => new AccountKey(platformId, participant.Puuid))
                .Distinct()
                .ToArray(),
            ct);

        var mapped = RiotMatchMapper.Map(matchDto, platformId, participantAccounts);

        session.Matches.Add(mapped.Match);
        session.MatchParticipants.AddRange(mapped.Participants);

        var perkSelections = mapped.PerkSelections.Select(selection => new ParticipantPerkSelection
        {
            MatchId = selection.MatchId,
            ParticipantId = selection.ParticipantId,
            PerkSelectionCatalogId = catalogIds[selection.Key]
        });

        session.MatchParticipants.AddPerkSelections(perkSelections);
    }
}
