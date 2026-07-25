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
        // MaxCandidatesPerRun caps each class on each platform there; the run-wide budget is
        // applied below, once, across the union.
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            options.Platforms,
            options.QueueId,
            options.MinObservedGames,
            options.MaxCandidatesPerRun,
            sinceUtc,
            ct);

        var rows = SelectWithinBudget(batch, options);
        var coverage = BuildCoverage(batch, rows);

        if (rows.Count == 0)
        {
            return new HarvestResult(0, 0, 0, coverage);
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
        // Counts entities written since the last flush — a new puuid contributes both an
        // account and a candidate, so a single row can add 2. A Skipped candidate adds 0
        // (or 1 when its puuid was new and only the account was created).
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

        return new HarvestResult(inserted, updated, accountsCreated, coverage);
    }

    /// <summary>
    /// Spends the run's budget across the two classes the repository tagged (#495).
    ///
    /// Ordering the whole eligible pool by observed games and cutting at
    /// <see cref="HarvestOptions.MaxCandidatesPerRun"/> is self-defeating once the pool
    /// outgrows the cap: the head of that order is the most-observed players, i.e. exactly
    /// the ones already harvested, so the run spends its entire budget refreshing known
    /// candidates and a pair that just crossed the observed-games gate never gets in.
    /// Reserving <see cref="HarvestOptions.NewCandidateShare"/> of the budget for pairs with
    /// no candidate yet guarantees discovery keeps moving, and because harvesting a pair
    /// moves it to the known class, that reservation drains a different slice of the backlog
    /// every run instead of re-reading the same head.
    ///
    /// The reservation is a floor, not a partition: each class may use whatever the other
    /// leaves unused, so the run still fills its budget when one class is short.
    /// </summary>
    private static List<HarvestedCandidateRow> SelectWithinBudget(
        HarvestCandidateBatch batch,
        HarvestOptions options)
    {
        var budget = Math.Max(1, options.MaxCandidatesPerRun);
        var newShare = Math.Clamp(options.NewCandidateShare, 0d, 1d);

        var newRows = ByPriority(batch.Rows.Where(row => !row.IsKnownCandidate));
        var knownRows = ByPriority(batch.Rows.Where(row => row.IsKnownCandidate));

        var reservedForNew = Math.Clamp((int)Math.Ceiling(budget * newShare), 0, budget);
        var takeNew = Math.Min(newRows.Count, Math.Max(reservedForNew, budget - knownRows.Count));
        var takeKnown = Math.Min(knownRows.Count, budget - takeNew);

        // New pairs first: they are the run's priority, and a failure part-way through leaves
        // discovery advanced rather than only stats refreshed.
        var selected = new List<HarvestedCandidateRow>(takeNew + takeKnown);
        selected.AddRange(newRows.Take(takeNew));
        selected.AddRange(knownRows.Take(takeKnown));
        return selected;
    }

    // Most-observed first, then most recently seen. The puuid/champion tiebreak only makes
    // the cut deterministic when those two collide.
    private static List<HarvestedCandidateRow> ByPriority(IEnumerable<HarvestedCandidateRow> rows)
        => rows
            .OrderByDescending(row => row.ObservedGames)
            .ThenByDescending(row => row.LastSeenUtc)
            .ThenBy(row => row.Puuid, StringComparer.Ordinal)
            .ThenBy(row => row.ChampionId)
            .ToList();

    /// <summary>
    /// Pairs the exact eligible counts (computed over the full aggregate, before any cap)
    /// with what the budget actually took, so <c>HarvestProcess</c> can report the shortfall
    /// instead of truncating silently.
    /// </summary>
    private static HarvestCoverage BuildCoverage(
        HarvestCandidateBatch batch,
        List<HarvestedCandidateRow> selected)
    {
        var selectedByPlatform = selected
            .GroupBy(row => row.PlatformId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (New: group.Count(row => !row.IsKnownCandidate), Known: group.Count(row => row.IsKnownCandidate)),
                StringComparer.Ordinal);

        var platforms = batch.Eligibility
            .Select(platform =>
            {
                selectedByPlatform.TryGetValue(platform.PlatformId, out var taken);
                return new HarvestPlatformCoverage(
                    platform.PlatformId,
                    platform.EligibleNew,
                    taken.New,
                    platform.EligibleKnown,
                    taken.Known);
            })
            .ToList();

        return new HarvestCoverage(
            platforms.Sum(platform => platform.EligibleNew),
            platforms.Sum(platform => platform.SelectedNew),
            platforms.Sum(platform => platform.EligibleKnown),
            platforms.Sum(platform => platform.SelectedKnown),
            platforms);
    }

    private static void AddMinimalAccount(IDataSession session, HarvestedCandidateRow row, DateTime nowUtc)
    {
        // Minimal account: puuid + platform only. GameName/TagLine left blank so the
        // account lands in AccountRefreshProcess's identity-backfill bucket (capped
        // priority 0 across all accounts, #788) to be resolved via account-v1.
        // CreatedAtUtc/UpdatedAtUtc fall back to their now() DB defaults.
        session.RiotAccounts.Add(new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = row.Puuid,
            PlatformId = row.PlatformId,
            // Explicit rather than relying on the entity default: a NOT NULL column, so an
            // implicit null would only surface as a DbUpdateException at save time.
            GameName = string.Empty,
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
        // re-scores it. In-flight (Queued/Processing) and Validated candidates keep their
        // state — they are already in or through the pipeline.
        //
        // Rejected stays rejected by design: a rejection is a verdict from real history
        // ingestion + MainAnalysis (play-rate over the account's actual ~50 games), not from
        // this biased participant sample. A bigger observed sample here is still a prior, so
        // it must not resurrect an account real history already ruled out — re-queuing would
        // just re-ingest and re-reject. (If we ever want to reconsider rejections past a much
        // higher observed threshold, that is a separate, explicit policy change.)
        if (existing.Status == MainCandidateStatus.Scored)
        {
            existing.Status = MainCandidateStatus.New;
            existing.ScoredAtUtc = null;
            // Clear the now-stale score too, so a row read between this pass and the next
            // ScoringProcess pass reflects "not yet scored" rather than the old value.
            existing.Score = 0;
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
