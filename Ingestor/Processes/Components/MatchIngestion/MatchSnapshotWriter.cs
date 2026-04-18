using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;

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
        CancellationToken ct)
    {
        var allMatchIds = (await riotMatchClient.GetMatchIdsAsync(puuid, region, matchesPerAccount, ct))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var trackedAccount = await session.RiotAccounts.GetByKeyAsync(platformId, puuid, ct);
        var scan = await ExistingMatchScanner.ScanAsync(session, allMatchIds, ct);
        var batchSize = Math.Max(1, saveBatchSize);

        if (trackedAccount is not null)
        {
            await MatchAccountBackfiller.BackfillAsync(session, scan.Existing, puuid, trackedAccount.Id, batchSize, ct);
        }

        var inserted = 0;
        for (var i = 0; i < scan.Fresh.Count; i += batchSize)
        {
            foreach (var matchId in scan.Fresh.Skip(i).Take(batchSize))
            {
                var matchDto = await riotMatchClient.GetMatchAsync(matchId, region, ct);
                await PersistMatchAsync(session, matchDto, platformId, ct);
                inserted++;
            }

            await session.SaveChangesAsync(ct);
        }

        return new SnapshotIngestionResult(allMatchIds, scan.Fresh, inserted, allMatchIds.Count - scan.Fresh.Count);
    }

    private static async Task PersistMatchAsync(
        IDataSession session,
        Ingestor.Riot.Dto.RiotMatchDto matchDto,
        string platformId,
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

        var catalogIdsByKey = await session.MatchParticipants.GetOrCreatePerkCatalogIdsAsync(
            mapped.PerkSelections.Select(selection => selection.Key).ToArray(),
            ct);

        var perkSelections = mapped.PerkSelections.Select(selection => new ParticipantPerkSelection
        {
            MatchId = selection.MatchId,
            ParticipantId = selection.ParticipantId,
            PerkSelectionCatalogId = catalogIdsByKey[selection.Key]
        });

        session.MatchParticipants.AddPerkSelections(perkSelections);
    }
}
