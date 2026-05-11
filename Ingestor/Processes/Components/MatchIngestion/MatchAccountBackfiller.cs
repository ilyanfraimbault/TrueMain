using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

internal static class MatchAccountBackfiller
{
    public static async Task BackfillAsync(
        IDataSession session,
        IReadOnlyCollection<string> existingMatchIds,
        string trackedPuuid,
        Guid trackedRiotAccountId,
        int batchSize,
        CancellationToken ct)
    {
        if (existingMatchIds.Count == 0)
        {
            return;
        }

        var participantsToUpdate = (await session.MatchParticipants.GetByMatchIdsAsync(existingMatchIds, ct))
            .Where(participant =>
                participant.RiotAccountId == null &&
                string.Equals(participant.Puuid, trackedPuuid, StringComparison.Ordinal))
            .ToList();

        var pendingUpdates = 0;

        foreach (var trackedParticipant in participantsToUpdate)
        {
            trackedParticipant.RiotAccountId = trackedRiotAccountId;
            pendingUpdates++;

            if (pendingUpdates < batchSize)
            {
                continue;
            }

            await session.SaveChangesAsync(ct);
            pendingUpdates = 0;
        }

        if (pendingUpdates > 0)
        {
            await session.SaveChangesAsync(ct);
        }
    }
}
