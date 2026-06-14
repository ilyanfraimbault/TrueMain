using Data.Entities;
using Data.Repositories;
using Ingestor.Options;

namespace Ingestor.Processes.Components.Discovery;

/// <summary>
/// Turns orphan <c>match_participants</c> rows (untracked players we already
/// persisted at zero extra Riot cost) into <see cref="MainCandidate"/>s (#485).
///
/// The observed (puuid, champion) play sample is a biased prior — we only see a
/// player's games when they shared a lobby with a tracked account — so harvested
/// candidates are NOT marked as mains here. They are queued like any other
/// candidate and only confirmed/rejected later by real history ingestion +
/// <c>MainAnalysis</c>.
///
/// Match ingestion claims <see cref="RiotAccount"/> rows (not raw puuids), so each
/// harvested puuid also gets a minimal account (puuid + platform only); its Riot ID
/// identity is left blank for <c>AccountRefreshProcess</c> to backfill.
/// </summary>
public sealed class ParticipantHarvestService : IParticipantHarvestService
{
    public async Task<HarvestResult> HarvestAsync(
        IDataSession session,
        HarvestOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        // Bound the scan to the configured lookback window (0 disables → scan all). UnixEpoch
        // is a safe far-past UTC sentinel for the "no filter" case (LoL predates nothing here).
        var sinceUtc = options.LookbackDays > 0 ? nowUtc.AddDays(-options.LookbackDays) : DateTime.UnixEpoch;

        // MinObservedGames/MaxCandidatesPerRun are validated > 0 at startup and clamped by
        // the repository, so pass them through here — the repository is the single guard.
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            options.Platforms,
            options.QueueId,
            options.MinObservedGames,
            options.MaxCandidatesPerRun,
            sinceUtc,
            ct);

        if (rows.Count == 0)
        {
            return new HarvestResult(0, 0, 0);
        }

        var saveBatchSize = Math.Max(1, options.SaveBatchSize);

        // Preload everything the loop would otherwise read per row, turning an O(N) chain
        // of round-trips into two queries: the candidates we might refresh and the puuids
        // that already have an account. Both are keyed for O(1) in-loop lookups; the
        // ensured set then also absorbs accounts we Add this run (the unique Puuid index
        // would otherwise reject a second insert for a puuid seen on another champion).
        var platformIds = rows.Select(row => row.PlatformId).Distinct(StringComparer.Ordinal).ToArray();
        var puuids = rows.Select(row => row.Puuid).Distinct(StringComparer.Ordinal).ToArray();

        var existingCandidates = (await session.MainCandidates
                .GetByPlatformsAndPuuidsAsync(platformIds, puuids, ct))
            .ToDictionary(CandidateKey, candidate => candidate);
        var ensuredPuuids = await session.RiotAccounts.GetExistingPuuidsAsync(puuids, ct);

        var inserted = 0;
        var updated = 0;
        var accountsCreated = 0;
        var pendingWrites = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (ensuredPuuids.Add(row.Puuid))
            {
                AddMinimalAccount(session, row, nowUtc);
                accountsCreated++;
                pendingWrites++;
            }

            switch (UpsertCandidate(session, existingCandidates, row, nowUtc))
            {
                case UpsertOutcome.Inserted:
                    inserted++;
                    pendingWrites++;
                    break;
                case UpsertOutcome.Updated:
                    updated++;
                    pendingWrites++;
                    break;
                case UpsertOutcome.Skipped:
                    break;
            }

            if (pendingWrites >= saveBatchSize)
            {
                await session.SaveChangesAsync(ct);
                pendingWrites = 0;
            }
        }

        if (pendingWrites > 0)
        {
            await session.SaveChangesAsync(ct);
        }

        return new HarvestResult(inserted, updated, accountsCreated);
    }

    private static void AddMinimalAccount(IDataSession session, HarvestedCandidateRow row, DateTime nowUtc)
    {
        // Minimal account: puuid + platform only. GameName/TagLine left blank so the
        // account is priority-0 for AccountRefreshProcess (identity backfill via
        // account-v1). CreatedAtUtc/UpdatedAtUtc fall back to their now() DB defaults.
        session.RiotAccounts.Add(new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = row.Puuid,
            PlatformId = row.PlatformId,
            UpdatedAtUtc = nowUtc,
            MatchIngestStatus = MatchIngestStatus.Idle
        });
    }

    private static UpsertOutcome UpsertCandidate(
        IDataSession session,
        Dictionary<(string, string, int), MainCandidate> existingCandidates,
        HarvestedCandidateRow row,
        DateTime nowUtc)
    {
        if (!existingCandidates.TryGetValue(CandidateKey(row), out var existing))
        {
            var candidate = new MainCandidate
            {
                PlatformId = row.PlatformId,
                Puuid = row.Puuid,
                ChampionId = row.ChampionId,
                Source = MainCandidateSource.Harvest,
                ObservedGames = row.ObservedGames,
                ObservedWins = row.ObservedWins,
                LastPlayTimeUtc = row.LastSeenUtc,
                DiscoveredAtUtc = nowUtc,
                Status = MainCandidateStatus.New
            };
            session.MainCandidates.Add(candidate);
            // Guard against a duplicate insert if the same (platform, puuid, champion)
            // somehow recurs in this run (it should not — the aggregation groups by it).
            existingCandidates[CandidateKey(row)] = candidate;
            return UpsertOutcome.Inserted;
        }

        // Only harvested candidates carry observed stats — leave a ladder/manual candidate's
        // fields untouched so the "observed stats are 0 outside Harvest" invariant holds and
        // its mastery recency (LastPlayTimeUtc) is not clobbered.
        if (existing.Source != MainCandidateSource.Harvest)
        {
            return UpsertOutcome.Skipped;
        }

        existing.ObservedGames = row.ObservedGames;
        existing.ObservedWins = row.ObservedWins;
        existing.LastPlayTimeUtc = row.LastSeenUtc;

        // Re-score on the refreshed sample: a harvested candidate that was Scored but never
        // promoted should compete again now that it has accumulated more observed games (its
        // stored score is stale otherwise). Reset to New so the same-pass ScoringProcess
        // re-scores it. In-flight (Queued/Processing), Validated and Rejected candidates keep
        // their state — they are already in or through the pipeline / explicitly decided.
        if (existing.Status == MainCandidateStatus.Scored)
        {
            existing.Status = MainCandidateStatus.New;
            existing.ScoredAtUtc = null;
        }

        return UpsertOutcome.Updated;
    }

    private static (string, string, int) CandidateKey(MainCandidate candidate)
        => (candidate.PlatformId, candidate.Puuid, candidate.ChampionId);

    private static (string, string, int) CandidateKey(HarvestedCandidateRow row)
        => (row.PlatformId, row.Puuid, row.ChampionId);

    private enum UpsertOutcome
    {
        Inserted,
        Updated,
        Skipped
    }
}
