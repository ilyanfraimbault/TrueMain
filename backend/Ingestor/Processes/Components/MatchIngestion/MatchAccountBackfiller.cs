using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

internal static class MatchAccountBackfiller
{
    public static Task BackfillAsync(
        IDataSession session,
        IReadOnlyCollection<string> existingMatchIds,
        string trackedPuuid,
        Guid trackedRiotAccountId,
        CancellationToken ct)
    {
        if (existingMatchIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Single set-based UPDATE instead of loading every participant row and
        // issuing per-batch SaveChangesAsync — one round trip fills the orphan
        // RiotAccountId values for the tracked puuid across all existing matches.
        return session.MatchParticipants.BackfillRiotAccountIdAsync(
            existingMatchIds,
            trackedPuuid,
            trackedRiotAccountId,
            ct);
    }
}
