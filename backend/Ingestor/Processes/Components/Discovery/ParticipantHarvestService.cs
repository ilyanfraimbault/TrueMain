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
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            options.Platforms,
            options.QueueId,
            Math.Max(1, options.MinObservedGames),
            Math.Max(1, options.MaxCandidatesPerRun),
            ct);

        if (rows.Count == 0)
        {
            return new HarvestResult(0, 0, 0);
        }

        var saveBatchSize = Math.Max(1, options.SaveBatchSize);

        // Puuids appear once per champion (the aggregation groups by puuid+champion),
        // so a single puuid can yield several rows. GetByPuuidAsync queries the DB and
        // won't see an account we Added but haven't saved yet, so track ensured puuids
        // in-process to avoid a duplicate insert against the unique Puuid index.
        var ensuredPuuids = new HashSet<string>(StringComparer.Ordinal);
        var inserted = 0;
        var updated = 0;
        var accountsCreated = 0;
        var pendingChanges = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (ensuredPuuids.Add(row.Puuid))
            {
                if (await EnsureAccountAsync(session, row, nowUtc, ct))
                {
                    accountsCreated++;
                }
            }

            if (await UpsertCandidateAsync(session, row, nowUtc, ct))
            {
                inserted++;
            }
            else
            {
                updated++;
            }

            pendingChanges++;
            if (pendingChanges >= saveBatchSize)
            {
                await session.SaveChangesAsync(ct);
                pendingChanges = 0;
            }
        }

        if (pendingChanges > 0)
        {
            await session.SaveChangesAsync(ct);
        }

        return new HarvestResult(inserted, updated, accountsCreated);
    }

    private static async Task<bool> EnsureAccountAsync(
        IDataSession session,
        HarvestedCandidateRow row,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existing = await session.RiotAccounts.GetByPuuidAsync(row.Puuid, ct);
        if (existing is not null)
        {
            return false;
        }

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
        return true;
    }

    private static async Task<bool> UpsertCandidateAsync(
        IDataSession session,
        HarvestedCandidateRow row,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existing = (await session.MainCandidates
                .GetByPlatformPuuidAndChampionsAsync(row.PlatformId, row.Puuid, [row.ChampionId], ct))
            .FirstOrDefault();

        if (existing is null)
        {
            session.MainCandidates.Add(new MainCandidate
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
            });
            return true;
        }

        // Refresh the observed signal so scoring stays fresh. Never touch a non-harvest
        // candidate's mastery fields/recency (LastPlayTimeUtc is mastery last-play there),
        // and never change Status/Source — a candidate already past New keeps its place
        // in the pipeline (mirrors ManualSeed's status discipline).
        existing.ObservedGames = row.ObservedGames;
        existing.ObservedWins = row.ObservedWins;
        if (existing.Source == MainCandidateSource.Harvest)
        {
            existing.LastPlayTimeUtc = row.LastSeenUtc;
        }

        return false;
    }
}
