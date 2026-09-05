using Core.Lol.Identifiers;
using Data.Entities;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.LadderSync;
using Ingestor.Processes.Summaries;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Keeps the stored rank of accounts we already track in step with the live ladder, by reading
/// the ladder itself instead of one account at a time (#1312).
/// </summary>
/// <remarks>
/// <para>
/// <c>AccountRefreshProcess</c> spends one league-v4 by-puuid call per account, which caps the
/// whole fleet at a few thousand refreshes a day and leaves LP days stale. The ladder endpoints
/// inverted the same question: one call returns a whole apex tier, and one paginated call returns
/// ~205 consecutive players of a division. Matching those entries against accounts we already
/// store is then a pure SQL join, so the marginal Riot cost of refreshing an account we happen to
/// see is zero.
/// </para>
/// <para>
/// The three apex tiers are re-read whole — nine calls covers Master+ across three platforms —
/// on the cadence of <see cref="LadderSyncOptions.ApexRefreshInterval"/>, while everything below
/// Master is walked incrementally under <see cref="LadderSyncOptions.MaxRequestsPerRun"/> and
/// <see cref="LadderSyncOptions.MaxRequestsPerDay"/>, resuming from a persisted cursor. The whole
/// process runs no more often than <see cref="LadderSyncOptions.MinRunInterval"/> (#1474): a
/// ladder moves slowly, and every iteration spent re-reading it is fetch-lane time taken from
/// match ingestion.
/// </para>
/// <para>
/// This process <em>never inserts accounts</em>. Seeding every player of every swept division
/// would add millions of rows and swamp the downstream pipeline; discovery is
/// <see cref="DiscoveryProcess"/>'s job and stays scoped to the apex ladders.
/// </para>
/// <para>
/// Accounts that fall out of the swept range need no special handling: they are simply not seen,
/// so <see cref="RiotAccount.LastRankSyncAtUtc"/> does not advance and <c>AccountRefreshProcess</c>
/// picks them up in its normal rotation, which is also what re-detects a demotion.
/// </para>
/// </remarks>
public sealed class LadderSyncProcess(
    ILogger<LadderSyncProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    IRankSnapshotWriter rankSnapshotWriter,
    IProcessRunStore processRunStore,
    TimeProvider timeProvider,
    IOptions<LadderSyncOptions> ladderSyncOptions) : IIngestorProcess
{
    private const string RankedSoloQueue = "RANKED_SOLO_5x5";

    public string Name => "LadderSync";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var options = ladderSyncOptions.Value;
        var platforms = PlatformNormalizer.Normalize(options.Platforms);

        if (platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (LadderSync:Platforms).");
            return new NoWorkSummary("No platforms configured.", 0);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Same guard as Discovery's (#487, #1149): measured from the last run that did its
        // work, so a skip can never re-arm itself. The current run is recorded as Running and
        // is therefore excluded from the answer.
        if (options.MinRunInterval > TimeSpan.Zero)
        {
            var lastRunUtc = await processRunStore.GetLastCompletedRunStartAsync(Name, ct);
            if (lastRunUtc is not null && nowUtc - lastRunUtc.Value < options.MinRunInterval)
            {
                logger.LogInformation(
                    "Ladder sync skipped: last run {LastRunUtc:o} is within MinRunInterval {Interval}.",
                    lastRunUtc,
                    options.MinRunInterval);
                return new SkippedSummary("Within MinRunInterval; ladder sync skipped this iteration.", true);
            }
        }

        var ledger = await LadderSyncRunLedger.ReadAsync(processRunStore, Name, options, nowUtc, ct);
        var apexDue = ledger.IsApexDue(options.ApexRefreshInterval, nowUtc);
        var budget = ledger.RemainingBudget(options);

        var apexTiers = LadderSweepPlan.ApexTiersInScope(options.TierScope);
        var slots = LadderSweepPlan.BuildSlots(options.TierScope);

        if (!apexDue && (budget == 0 || slots.Count == 0))
        {
            logger.LogInformation(
                "Ladder sync skipped: daily budget spent ({SpentToday}/{MaxRequestsPerDay}) and the apex refresh is not due.",
                ledger.PagedCallsToday,
                options.MaxRequestsPerDay);
            return new SkippedSummary("Daily request budget spent and apex refresh not due; ladder sync skipped this iteration.", true);
        }

        var stats = new LadderSweepStats();
        var buffer = new EntryBuffer(Math.Max(1, options.SaveBatchSize));

        if (apexDue)
        {
            await SyncApexTiersAsync(platforms, apexTiers, buffer, stats, ct);
        }

        await SweepDivisionsAsync(platforms, slots, budget, buffer, stats, ct);
        await buffer.FlushAsync(this, stats, ct);

        var summary = stats.ToSummary();
        logger.LogInformation(
            "Ladder sync summary: apexCalls={ApexCalls}, pagedCalls={PagedCalls}, entries={Entries}, matched={Matched}, inserted={Inserted}, updated={Updated}, unchanged={Unchanged}, failedCalls={FailedCalls}.",
            summary.ApexCalls,
            summary.PagedCalls,
            summary.EntriesFetched,
            summary.AccountsMatched,
            summary.RankInserted,
            summary.RankUpdated,
            summary.RankUnchanged,
            summary.FailedCalls);

        return summary;
    }

    /// <summary>
    /// Re-reads every configured apex ladder in full. One call per (platform, tier), outside the
    /// paginated budget: at nine calls for three platforms the Riot cost is negligible. What is
    /// not negligible is joining tens of thousands of Master entries against the account table,
    /// which is why it runs on <see cref="LadderSyncOptions.ApexRefreshInterval"/> rather than
    /// on every run.
    /// </summary>
    private async Task SyncApexTiersAsync(
        IReadOnlyList<string> platforms,
        IReadOnlyList<string> apexTiers,
        EntryBuffer buffer,
        LadderSweepStats stats,
        CancellationToken ct)
    {
        foreach (var platformString in platforms)
        {
            if (!PlatformId.TryParse(platformString, out var platform))
            {
                logger.LogWarning("Skipping unknown platform '{Platform}'.", platformString);
                continue;
            }

            foreach (var tier in apexTiers)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var league = await FetchApexLadderAsync(platform.Route, tier, ct);
                    stats.ApexCalls++;

                    foreach (var entry in league.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Puuid) || string.IsNullOrWhiteSpace(entry.Rank))
                        {
                            continue;
                        }

                        // The apex LeagueItemDTO carries no tier of its own — the parent league
                        // list holds it — so prefer the list's tier and fall back to the
                        // requested one when Riot omits it.
                        var resolvedTier = string.IsNullOrWhiteSpace(league.Tier) ? tier : league.Tier!;
                        stats.Count(resolvedTier);
                        buffer.Add(platformString, entry.Puuid!, resolvedTier, entry.Rank!, entry.LeaguePoints, entry.Wins, entry.Losses);
                    }

                    await buffer.FlushIfFullAsync(this, stats, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One tier or one platform hitting a wall must not cost the others their
                    // refresh; the next run re-reads the whole ladder anyway.
                    stats.FailedCalls++;
                    logger.LogWarning(ex, "Failed to read the {Tier} ladder for {Platform}.", tier, platformString);
                }
            }
        }
    }

    private Task<RiotLeagueListDto> FetchApexLadderAsync(PlatformRoute platform, string tier, CancellationToken ct)
        => tier switch
        {
            "CHALLENGER" => riotPlatformClient.GetChallengerLeagueAsync(platform, RankedSoloQueue, ct),
            "GRANDMASTER" => riotPlatformClient.GetGrandmasterLeagueAsync(platform, RankedSoloQueue, ct),
            _ => riotPlatformClient.GetMasterLeagueAsync(platform, RankedSoloQueue, ct)
        };

    /// <summary>
    /// Walks the paginated per-division ladders under a shared request budget, one page per
    /// platform per turn.
    /// </summary>
    /// <remarks>
    /// Round-robin rather than platform-by-platform on purpose: the budget is expected to run out
    /// mid-sweep, and draining one platform first would mean the last platform in the list never
    /// advances at all — the same region-blind allocation that let the account pool drift to ~82 %
    /// one region (#1149/#1150).
    /// </remarks>
    private async Task SweepDivisionsAsync(
        IReadOnlyList<string> platforms,
        IReadOnlyList<LadderSweepSlot> slots,
        int budget,
        EntryBuffer buffer,
        LadderSweepStats stats,
        CancellationToken ct)
    {
        if (budget <= 0 || slots.Count == 0)
        {
            return;
        }

        await using var cursorSession = await sessionFactory.CreateAsync(ct);

        var states = new List<PlatformSweepState>();
        foreach (var platformString in platforms)
        {
            if (!PlatformId.TryParse(platformString, out var platform))
            {
                continue;
            }

            var cursor = await cursorSession.LadderSyncCursors.GetAsync(platformString, ct);
            var slotIndex = LadderSweepPlan.IndexOfOrStart(
                slots,
                cursor is null ? null : new LadderSweepSlot(cursor.Tier, cursor.Division));

            states.Add(new PlatformSweepState(platformString, platform.Route, slotIndex, Math.Max(1, cursor?.Page ?? 1)));
        }

        var spent = 0;
        while (spent < budget && states.Count > 0)
        {
            foreach (var state in states)
            {
                if (spent >= budget)
                {
                    break;
                }

                ct.ThrowIfCancellationRequested();

                var slot = slots[state.SlotIndex];
                var page = state.Page;

                // Advance and persist the cursor BEFORE the fetch, for the same reason the
                // discovery cursor does (#486): a page that fails deterministically would
                // otherwise pin the sweep on it forever. Losing one page per sweep to a
                // transient failure is the cheaper trade — the wrap picks it up next time.
                state.Page = page + 1;
                await PersistCursorAsync(cursorSession, state, slots, ct);

                spent++;
                List<RiotLeagueDivisionEntryDto> entries;
                try
                {
                    entries = await riotPlatformClient.GetLeagueEntriesAsync(
                        state.Route,
                        RankedSoloQueue,
                        slot.Tier,
                        slot.Division,
                        page,
                        ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stats.FailedCalls++;
                    logger.LogWarning(
                        ex,
                        "Failed to read {Platform} {Tier} {Division} page {Page}.",
                        state.PlatformId,
                        slot.Tier,
                        slot.Division,
                        page);
                    continue;
                }

                stats.PagedCalls++;

                if (entries.Count == 0)
                {
                    // End of the division. Riot answers a page past the end with an empty array,
                    // which is a more robust stop condition than comparing against an assumed
                    // page size — it costs one extra call per division per sweep and makes no
                    // assumption about Riot's paging width.
                    state.SlotIndex = (state.SlotIndex + 1) % slots.Count;
                    state.Page = 1;
                    await PersistCursorAsync(cursorSession, state, slots, ct);
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Puuid) || string.IsNullOrWhiteSpace(entry.Rank))
                    {
                        continue;
                    }

                    var tier = string.IsNullOrWhiteSpace(entry.Tier) ? slot.Tier : entry.Tier!;
                    stats.Count(tier);
                    buffer.Add(state.PlatformId, entry.Puuid!, tier, entry.Rank!, entry.LeaguePoints, entry.Wins, entry.Losses);
                }

                await buffer.FlushIfFullAsync(this, stats, ct);
            }
        }
    }

    private Task PersistCursorAsync(
        IDataSession session,
        PlatformSweepState state,
        IReadOnlyList<LadderSweepSlot> slots,
        CancellationToken ct)
    {
        var slot = slots[state.SlotIndex];
        return session.LadderSyncCursors.UpsertAsync(
            state.PlatformId,
            slot.Tier,
            slot.Division,
            state.Page,
            timeProvider.GetUtcNow().UtcDateTime,
            ct);
    }

    /// <summary>
    /// Joins one buffered slice of ladder entries against the accounts we already store and
    /// writes their snapshots. Entries with no matching account are dropped, not inserted.
    /// </summary>
    private async Task<FlushOutcome> FlushAsync(IReadOnlyList<BufferedEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return default;
        }

        await using var session = await sessionFactory.CreateAsync(ct);

        var keys = entries.Select(entry => new AccountKey(entry.PlatformId, entry.Puuid)).ToList();
        var accountsByKey = await session.RiotAccounts.GetByKeysAsync(keys, ct);
        if (accountsByKey.Count == 0)
        {
            return default;
        }

        var latestByAccountId = await session.RankSnapshots.GetLatestForAccountsAsync(
            accountsByKey.Values.Select(account => account.Id).ToList(),
            ct);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var outcome = new FlushOutcome();

        foreach (var entry in entries)
        {
            if (!accountsByKey.TryGetValue(new AccountKey(entry.PlatformId, entry.Puuid), out var account))
            {
                continue;
            }

            latestByAccountId.TryGetValue(account.Id, out var latest);
            var result = rankSnapshotWriter.Ingest(
                session,
                account,
                new RankSnapshotInput(entry.Tier, entry.Division, entry.LeaguePoints, entry.Wins, entry.Losses),
                latest,
                nowUtc);

            outcome.Matched++;
            switch (result)
            {
                case RankSnapshotOutcome.Inserted:
                    outcome.Inserted++;
                    break;
                case RankSnapshotOutcome.Updated:
                    outcome.Updated++;
                    break;
                default:
                    outcome.Unchanged++;
                    break;
            }
        }

        await session.SaveChangesAsync(ct);
        return outcome;
    }

    private sealed record BufferedEntry(
        string PlatformId,
        string Puuid,
        string Tier,
        string Division,
        int LeaguePoints,
        int Wins,
        int Losses);

    private struct FlushOutcome
    {
        public int Matched;
        public int Inserted;
        public int Updated;
        public int Unchanged;
    }

    private sealed class PlatformSweepState(string platformId, PlatformRoute route, int slotIndex, int page)
    {
        public string PlatformId { get; } = platformId;
        public PlatformRoute Route { get; } = route;
        public int SlotIndex { get; set; } = slotIndex;
        public int Page { get; set; } = page;
    }

    /// <summary>
    /// Accumulates ladder entries across pages so the account join runs once per slice rather
    /// than once per page, keyed by (platform, puuid) so a player seen twice — pages shift under
    /// a live ladder — is written once, with the most recent reading.
    /// </summary>
    private sealed class EntryBuffer(int capacity)
    {
        private readonly Dictionary<AccountKey, BufferedEntry> _entries = [];

        public void Add(string platformId, string puuid, string tier, string division, int leaguePoints, int wins, int losses)
        {
            _entries[new AccountKey(platformId, puuid)] =
                new BufferedEntry(platformId, puuid, tier, division, leaguePoints, wins, losses);
        }

        public Task FlushIfFullAsync(LadderSyncProcess owner, LadderSweepStats stats, CancellationToken ct)
            => _entries.Count >= capacity ? FlushAsync(owner, stats, ct) : Task.CompletedTask;

        public async Task FlushAsync(LadderSyncProcess owner, LadderSweepStats stats, CancellationToken ct)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            var batch = _entries.Values.ToList();
            _entries.Clear();

            var outcome = await owner.FlushAsync(batch, ct);
            stats.AccountsMatched += outcome.Matched;
            stats.RankInserted += outcome.Inserted;
            stats.RankUpdated += outcome.Updated;
            stats.RankUnchanged += outcome.Unchanged;
        }
    }
}
