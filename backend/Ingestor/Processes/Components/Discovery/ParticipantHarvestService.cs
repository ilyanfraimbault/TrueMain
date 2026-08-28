using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Coverage;

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
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        CancellationToken ct)
    {
        // Bound the scan to the configured lookback window (0 disables → scan all). UnixEpoch
        // is a safe far-past UTC sentinel for the "no filter" case (LoL predates nothing here).
        var sinceUtc = options.LookbackDays > 0 ? nowUtc.AddDays(-options.LookbackDays) : DateTime.UnixEpoch;

        // MinObservedGames/MaxCandidatesPerRun are validated > 0 at startup and clamped by
        // the repository, so pass them through here — the repository is the single guard.
        // MaxCandidatesPerRun caps each class on each platform there; the run-wide budget is
        // split per platform below and then applied inside each slice (#1150).
        //
        // The full budget has to be the per-class cap, not a fraction of it: the reservation
        // spills, so either class may legitimately take the whole budget when the other is
        // short. Fetching less per class would let a row the run would have selected fall
        // outside the slice.
        //
        // So the worst case (every class saturated on every platform) materialises
        // platforms x 2 x MaxCandidatesPerRun rows before SelectWithinBudget re-caps —
        // 3 x 2 x 5000 = 30k rows at the shipped defaults. A row is ~280 B (a ~56 B record,
        // a ~180 B 78-char puuid string, a ~32 B platform string, an 8 B list slot), so
        // ~8 MB. That is three orders of magnitude below the ~6 GB heap that OOM-killed the
        // pattern aggregation (#600), and unlike that one it is flat: no timeline JSON, no
        // per-match rows, and the per-platform scan list is garbage after each iteration.
        //
        // It scales linearly with the option — roughly 1.7 MB per extra 1000 of
        // MaxCandidatesPerRun across three platforms, so raising it to 50k would cost ~85 MB
        // of transient heap per run. Fine at that order; a raise into the millions would need
        // this to stream instead of materialise.
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            options.Platforms,
            options.QueueId,
            options.MinObservedGames,
            options.MaxCandidatesPerRun,
            sinceUtc,
            ct);

        var rows = SelectWithinBudget(batch, options, coverage);
        var harvestCoverage = BuildCoverage(batch, rows);

        if (rows.Count == 0)
        {
            return new HarvestResult(0, 0, 0, harvestCoverage);
        }

        var saveBatchSize = Math.Max(1, options.SaveBatchSize);

        var inserted = 0;
        var updated = 0;
        var accountsCreated = 0;

        // One slice per save, each with its own preload and its own tracker drain (#1229).
        // The preload is what turns an O(N) chain of per-row round-trips into two queries
        // — the candidates we might refresh and the puuids that already have an account —
        // and it is scoped to the slice rather than to the run so those two properties can
        // coexist: the candidates it returns are TRACKED and mutated in place, so a
        // run-wide preload plus a per-slice ClearTracking would detach every candidate a
        // later slice still has to update and drop those writes silently.
        //
        // Slicing costs two extra queries per slice, not per row, and reading per slice is
        // correct across slices: a puuid inserted in an earlier slice is already committed,
        // so the account lookup below finds it and the unique Puuid index is never raced.
        for (var offset = 0; offset < rows.Count; offset += saveBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var slice = rows.GetRange(offset, Math.Min(saveBatchSize, rows.Count - offset));
            var platformIds = slice.Select(row => row.PlatformId).Distinct(StringComparer.Ordinal).ToArray();
            var puuids = slice.Select(row => row.Puuid).Distinct(StringComparer.Ordinal).ToArray();

            var existingCandidates = (await session.MainCandidates
                    .GetByPlatformsAndPuuidsAsync(platformIds, puuids, ct))
                .ToDictionary(CandidateKey, candidate => candidate);
            // The ensured set also absorbs the accounts we Add within this slice (the
            // unique Puuid index would otherwise reject a second insert for a puuid seen
            // on another champion).
            var ensuredPuuids = await session.RiotAccounts.GetExistingPuuidsAsync(puuids, ct);

            foreach (var row in slice)
            {
                ct.ThrowIfCancellationRequested();

                if (ensuredPuuids.Add(row.Puuid))
                {
                    AddMinimalAccount(session, row, nowUtc);
                    accountsCreated++;
                }

                switch (UpsertCandidate(session, existingCandidates, row, nowUtc))
                {
                    case UpsertOutcome.Inserted:
                        inserted++;
                        break;
                    case UpsertOutcome.Updated:
                        updated++;
                        break;
                    case UpsertOutcome.Skipped:
                        break;
                }
            }

            await session.SaveChangesAsync(ct);
            session.ClearTracking();
        }

        return new HarvestResult(inserted, updated, accountsCreated, harvestCoverage);
    }

    /// <summary>
    /// Spends the run's budget across platforms (#1150) and, inside each, across the two
    /// classes the repository tagged (#495).
    ///
    /// <para>
    /// Ordering the whole eligible pool by observed games and cutting at
    /// <see cref="HarvestOptions.MaxCandidatesPerRun"/> is self-defeating once the pool
    /// outgrows the cap: the head of that order is the most-observed players, i.e. exactly
    /// the ones already harvested, so the run spends its entire budget refreshing known
    /// candidates and a pair that just crossed MinObservedGames never reaches the window.
    /// Reserving <see cref="HarvestOptions.NewCandidateShare"/> of the budget for pairs with
    /// no candidate yet guarantees discovery keeps moving, and because harvesting a pair
    /// moves it to the known class, that reservation drains a different slice of the backlog
    /// every run instead of re-reading the same head.
    /// </para>
    ///
    /// <para>
    /// That single ordering had the same flaw one dimension up. Observed games come from the
    /// matches we already ingested, so the densest observations are always the region we
    /// ingest most, and a global order handed it most of the budget — the harvest could only
    /// ever reproduce the region mix that produced it. The budget is now split per platform
    /// by coverage deficit first, and the class split applies inside each platform's slice.
    /// </para>
    ///
    /// <para>
    /// Both splits are floors, not partitions: a class may use whatever the other leaves, and
    /// a platform that cannot fill its slice releases it to the platforms that can. So the run
    /// still fills its whole budget when one class — or one region — is short.
    /// </para>
    /// </summary>
    private static List<HarvestedCandidateRow> SelectWithinBudget(
        HarvestCandidateBatch batch,
        HarvestOptions options,
        ChampionCoverageSnapshot coverage)
    {
        var budget = Math.Max(1, options.MaxCandidatesPerRun);
        var newShare = Math.Clamp(options.NewCandidateShare, 0d, 1d);

        var platforms = batch.Rows
            .Select(row => row.PlatformId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(platform => platform, StringComparer.Ordinal)
            .ToList();

        if (platforms.Count == 0)
        {
            return [];
        }

        var quotas = PlatformBudgetAllocator.Allocate(platforms, budget, coverage);

        var newRows = new Dictionary<string, List<HarvestedCandidateRow>>(StringComparer.OrdinalIgnoreCase);
        var knownRows = new Dictionary<string, List<HarvestedCandidateRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var platform in platforms)
        {
            var onPlatform = batch.Rows
                .Where(row => string.Equals(row.PlatformId, platform, StringComparison.OrdinalIgnoreCase))
                .ToList();
            newRows[platform] = ByPriority(onPlatform.Where(row => !row.IsKnownCandidate));
            knownRows[platform] = ByPriority(onPlatform.Where(row => row.IsKnownCandidate));
        }

        var selected = new List<HarvestedCandidateRow>(budget);
        var takenNew = platforms.ToDictionary(platform => platform, _ => 0, StringComparer.OrdinalIgnoreCase);
        var takenKnown = platforms.ToDictionary(platform => platform, _ => 0, StringComparer.OrdinalIgnoreCase);

        // Pass 1 — each platform spends its own slice, new pairs first: they are the run's
        // priority, and a failure part-way through leaves discovery advanced rather than only
        // stats refreshed.
        foreach (var platform in platforms)
        {
            var quota = Math.Min(quotas.TryGetValue(platform, out var slot) ? slot : 0, budget);
            var reservedForNew = Math.Clamp((int)Math.Ceiling(quota * newShare), 0, quota);
            var takeNew = Math.Min(newRows[platform].Count, Math.Max(reservedForNew, quota - knownRows[platform].Count));
            var takeKnown = Math.Min(knownRows[platform].Count, quota - takeNew);

            Take(newRows[platform], takenNew, platform, takeNew);
            Take(knownRows[platform], takenKnown, platform, takeKnown);
        }

        // Pass 2 — spill, round-robin across platforms so an unfilled slice is shared rather
        // than handed whole to whichever platform sorts first (which is what a single global
        // ordering did).
        var progressed = true;
        while (selected.Count < budget && progressed)
        {
            progressed = false;
            foreach (var platform in platforms)
            {
                if (selected.Count >= budget)
                {
                    break;
                }

                if (Take(newRows[platform], takenNew, platform, 1) > 0
                    || Take(knownRows[platform], takenKnown, platform, 1) > 0)
                {
                    progressed = true;
                }
            }
        }

        return selected;

        int Take(
            List<HarvestedCandidateRow> source,
            Dictionary<string, int> cursorByPlatform,
            string platform,
            int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var cursor = cursorByPlatform[platform];
            var take = Math.Min(count, source.Count - cursor);
            if (take <= 0)
            {
                return 0;
            }

            selected.AddRange(source.GetRange(cursor, take));
            cursorByPlatform[platform] = cursor + take;
            return take;
        }
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
