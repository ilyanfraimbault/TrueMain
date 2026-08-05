using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.BuildFacts;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingestor.Processes;

/// <summary>
/// Incrementally pre-aggregates the champion powerspikes read (#694) so it stops
/// self-joining the dense per-minute <see cref="MatchParticipantTimelineSnapshot"/>
/// grid — which then becomes prunable down to the canonical marks.
///
/// Each match is folded exactly once (gated by <see cref="Match.PowerspikeAggregated"/>)
/// into three additive tables, replaying the read's own maths while the 30-minute
/// snapshots still exist:
/// <list type="bullet">
/// <item>the per-minute gold/damage lead over the lane opponent → the power curve
/// (<c>champion_powerspike_curve_stats</c>);</item>
/// <item>the slope-change spike of the opponent-relative power around each level
/// milestone (6/11/16) and each completed build item → the event spikes
/// (<c>champion_powerspike_event_stats</c>);</item>
/// <item>the global per-minute spread of the lead over every lane pair → the
/// normaliser (<c>powerspike_sigma_stats</c>).</item>
/// </list>
///
/// The power blend needs the global spread σ(m); it is accumulated here too, so
/// once the snapshots are pruned it can no longer be recomputed and becomes a
/// lifetime average rather than a live window. Within a run σ is refreshed from the
/// batch before the spikes that consume it are computed, so a single-batch run is
/// exact; across runs σ only converges (a slowly-changing per-minute scale on an
/// already-correlational feature).
///
/// Event rows are scoped to the core build the game belonged to (#890): the same
/// <c>(BuildItem0, PrimaryKeystoneId)</c> pair <c>ChampionBuildsQueryService</c>
/// groups its build tabs by, resolved here through the very same
/// <see cref="FinalBuildResolver"/> so the keys join. A champion built two ways
/// therefore yields two independent sets of item spikes rather than one blend.
/// Item events are the participant's own genuinely completed items — detected from
/// the purchase events, not from what happens to sit in the final inventory, which
/// would count a component that never got upgraded and miss an item completed then
/// sold. What counts as completed is
/// <see cref="FinalBuildResolver.IsEligibleFinalBuildItem"/>, the build path's own
/// rule: an item that cannot appear in a build cannot be that build's power spike
/// (#1021).
///
/// Event rows also carry the lane opponent the spike was measured against (#957).
/// The fold already resolves that opponent — the power series <em>is</em> the diff
/// against them — so this records a fact it used to discard rather than computing a
/// new one, and it is the only way the champion page's matchup filter can reach the
/// spikes: the ±<see cref="SpikeWindowMinutes"/> window needs the dense grid, which
/// retention prunes the moment this process flags the match.
/// </summary>
public sealed class ChampionPowerspikeAggregationProcess(
    ILogger<ChampionPowerspikeAggregationProcess> logger,
    IOptions<PowerspikeAggregationOptions> options,
    IOptions<MainAnalysisOptions> analysisOptions,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IItemMetadataProvider itemMetadataProvider,
    TimeProvider timeProvider) : IIngestorProcess
{
    private static readonly string[] CanonicalPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    private static readonly int[] LevelMilestones = [6, 11, 16];

    // Half-window (minutes) each side of an event for the slope-change spike.
    // Mirrors ChampionPowerspikesQueryService.
    private const int SpikeWindowMinutes = 3;

    public string Name => "ChampionPowerspikeAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var batches = 0;

        await PurgeIneligibleItemEventsAsync(queueId, ct);

        while (maxPerRun == 0 || processedMatches < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // Only matches whose timeline has been ingested carry snapshots; a match
            // still awaiting its timeline must not be flagged, or its contribution
            // would be lost. The partial index IX_matches_powerspike_pending keeps
            // this selection cheap once the backlog is drained.
            var matchIds = await db.Matches
                .AsNoTracking()
                .Where(m => m.QueueId == queueId && !m.PowerspikeAggregated && m.TimelineIngested)
                .OrderBy(m => m.Id)
                .Take(take)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (matchIds.Count == 0)
            {
                break;
            }

            await ProcessBatchAsync(db, queueId, matchIds, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        logger.LogInformation(
            "Champion powerspike aggregation summary: matches={Matches}, batches={Batches}.",
            processedMatches,
            batches);

        return new MatchAggregationSummary(processedMatches, batches);
    }

    /// <summary>
    /// Deletes item event rows for items that can never belong to a build path —
    /// the potions, trinkets, starters and support-quest items the pre-#1021 filter
    /// accepted. The read already hides them (it intersects with the core path), so
    /// this is storage hygiene rather than a correctness fix, and it is worth doing
    /// because those items are bought in nearly every game: they are a large,
    /// permanently unreadable share of the table.
    /// </summary>
    /// <remarks>
    /// Drains over as many runs as it needs, then costs one scan per run: the
    /// predicate is <c>(EventType, RefId)</c>, the trailing pair of the natural-key
    /// index, so Postgres can serve the steady-state "nothing left" answer index-only
    /// but cannot seek to it. That is the same bargain
    /// <see cref="RunePageDeduplicationProcess"/> makes, and the reason no index was
    /// added for it — a permanent write cost to speed up a cleanup that converges.
    /// </remarks>
    /// <remarks>
    /// Only ids the current patch's metadata <em>knows and rejects</em> are deleted.
    /// An id absent from that metadata is left alone on purpose: a reworked or
    /// removed item is unknown today yet was a legitimate spike on the patch it was
    /// folded under, and deleting it would rewrite history to match a rule it was
    /// never judged by.
    /// </remarks>
    private async Task PurgeIneligibleItemEventsAsync(int queueId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var latestGameVersion = await db.Matches
            .AsNoTracking()
            .Where(m => m.QueueId == queueId && m.GameVersion != "")
            .OrderByDescending(m => m.GameStartTimeUtc)
            .Select(m => m.GameVersion)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(latestGameVersion))
        {
            return;
        }

        var itemMetadata = await itemMetadataProvider.GetItemsAsync(latestGameVersion, ct);
        var ineligibleIds = itemMetadata.Values
            .Where(meta => !FinalBuildResolver.IsEligibleFinalBuildItem(meta))
            .Select(meta => meta.Id)
            .ToList();

        if (ineligibleIds.Count == 0)
        {
            return;
        }

        // Bounded batches, one transaction each, like the retention deletes (#982,
        // #988): the predicate is not index-aligned, so a single statement over the
        // whole table is the shape that blew the command timeout there. Each batch
        // commits its own progress, so an interrupted run resumes instead of redoing.
        const int deleteBatchSize = 5_000;
        var deleted = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batchIds = await db.ChampionPowerspikeEventStats
                .AsNoTracking()
                .Where(e => e.EventType == "item" && ineligibleIds.Contains(e.RefId))
                .OrderBy(e => e.Id)
                .Take(deleteBatchSize)
                .Select(e => e.Id)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
            {
                break;
            }

            deleted += await db.ChampionPowerspikeEventStats
                .Where(e => batchIds.Contains(e.Id))
                .ExecuteDeleteAsync(ct);
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "Purged {Deleted} powerspike item event rows for items that cannot belong to a build path.",
                deleted);
        }
    }

    private async Task ProcessBatchAsync(
        TrueMainDbContext db,
        int queueId,
        List<string> matchIds,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var versionByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion })
            .ToDictionaryAsync(m => m.Id, m => m.GameVersion, ct);

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.EloBracket,
                p.RiotAccountId != null,
                new[] { p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5, p.Item6 },
                p.ItemEvents))
            .ToListAsync(ct);

        var keystoneByParticipant = await LoadKeystonesAsync(db, matchIds, ct);

        var snapshotRows = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId))
            .Select(s => new
            {
                s.MatchId,
                s.ParticipantId,
                s.IntervalMinute,
                s.TotalGold,
                s.DamageToChampions,
                s.Level
            })
            .ToListAsync(ct);

        // (MatchId, ParticipantId) -> minute -> (gold, damage, level).
        var snapshotsByParticipant = new Dictionary<(string, int), Dictionary<int, ParticipantMinute>>();
        foreach (var s in snapshotRows)
        {
            var key = (s.MatchId, s.ParticipantId);
            if (!snapshotsByParticipant.TryGetValue(key, out var byMinute))
            {
                byMinute = new Dictionary<int, ParticipantMinute>();
                snapshotsByParticipant[key] = byMinute;
            }

            byMinute[s.IntervalMinute] = new ParticipantMinute(s.TotalGold, s.DamageToChampions, s.Level);
        }

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sigmaBatch = new Dictionary<int, SigmaAccumulator>();
        var curve = new Dictionary<CurveKey, CurveAccumulator>();
        var events = new Dictionary<EventKey, EventAccumulator>();

        // Pass 1: global per-minute spread over every lane pair (all champions,
        // both directions — mirrors the read's self-join population).
        foreach (var (matchId, parts) in participantsByMatch)
        {
            foreach (var group in parts.GroupBy(p => p.TeamPosition))
            {
                var laneParts = group.ToList();
                foreach (var a in laneParts)
                {
                    if (!snapshotsByParticipant.TryGetValue((matchId, a.ParticipantId), out var sa))
                    {
                        continue;
                    }

                    foreach (var b in laneParts)
                    {
                        if (b.TeamId == a.TeamId
                            || !snapshotsByParticipant.TryGetValue((matchId, b.ParticipantId), out var sb))
                        {
                            continue;
                        }

                        AccumulateSigma(sigmaBatch, sa, sb);
                    }
                }
            }
        }

        var sigmaByMinute = await MergeSigmaAsync(db, queueId, sigmaBatch, ct);

        // Pass 2: per tracked champion side, the curve diffs and the event spikes.
        foreach (var (matchId, parts) in participantsByMatch)
        {
            var gameVersion = versionByMatch.GetValueOrDefault(matchId);
            var patch = string.IsNullOrEmpty(gameVersion) ? null : PatchVersion.Normalize(gameVersion);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            // Patch-pinned item metadata: what counts as a completed item, and which
            // item opens the build, both move between patches.
            var itemMetadata = await itemMetadataProvider.GetItemsAsync(gameVersion!, ct);

            foreach (var p1 in parts)
            {
                if (!p1.Tracked || !CanonicalPositions.Contains(p1.TeamPosition))
                {
                    continue;
                }

                var opponent = parts.FirstOrDefault(p2 =>
                    p2.TeamPosition == p1.TeamPosition && p2.TeamId != p1.TeamId);
                if (opponent is null
                    || !snapshotsByParticipant.TryGetValue((matchId, p1.ParticipantId), out var s1)
                    || !snapshotsByParticipant.TryGetValue((matchId, opponent.ParticipantId), out var s2))
                {
                    continue;
                }

                // Per-minute lead series (intersection of both sides' marks).
                var series = new Dictionary<int, DiffMinute>();
                foreach (var (minute, m1) in s1)
                {
                    if (s2.TryGetValue(minute, out var m2))
                    {
                        series[minute] = new DiffMinute(m1.Gold - m2.Gold, m1.Damage - m2.Damage, m1.Level);
                    }
                }

                AccumulateCurve(curve, p1, patch, series);

                // The core build this game belonged to. Resolved exactly the way the
                // builds read resolves its tabs, so the keys join; a game whose build
                // or keystone can't be resolved contributes to the curve (which is
                // build-agnostic) but not to the per-build event spikes.
                var starterAnalysis = StarterItemAnalyzer.Analyze(p1.ItemEvents, p1.FinalItems, itemMetadata);
                var buildItems = FinalBuildResolver.Resolve(
                    p1.ItemEvents, p1.FinalItems, starterAnalysis.Items, itemMetadata);
                var firstItemId = buildItems.Length > 0 ? buildItems[0] : 0;
                var keystoneId = keystoneByParticipant.GetValueOrDefault((matchId, p1.ParticipantId));
                if (firstItemId <= 0 || keystoneId <= 0)
                {
                    continue;
                }

                AccumulateEvents(
                    events,
                    p1,
                    // The spike is already defined against this opponent — the series
                    // above is p1 minus them — so keying the aggregate on it records a
                    // fact the fold had in hand and used to throw away (#957). It is
                    // what lets the champion page's matchup filter reach the spikes
                    // without a second pipeline: no live recompute could, since the
                    // ±3-minute window needs the dense grid retention prunes.
                    opponent.ChampionId,
                    patch,
                    new BuildScope(firstItemId, keystoneId),
                    series,
                    sigmaByMinute,
                    itemMetadata);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await UpsertSigmaAsync(db, queueId, sigmaBatch, aggregatedAtUtc, ct);
        await UpsertCurveAsync(db, curve, aggregatedAtUtc, ct);
        await UpsertEventsAsync(db, events, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PowerspikeAggregated, true), ct);

        await transaction.CommitAsync(ct);
    }

    private static void AccumulateSigma(
        Dictionary<int, SigmaAccumulator> sigmaBatch,
        Dictionary<int, ParticipantMinute> a,
        Dictionary<int, ParticipantMinute> b)
    {
        foreach (var (minute, ma) in a)
        {
            if (!b.TryGetValue(minute, out var mb))
            {
                continue;
            }

            double goldDiff = ma.Gold - mb.Gold;
            double damageDiff = ma.Damage - mb.Damage;

            if (!sigmaBatch.TryGetValue(minute, out var acc))
            {
                acc = new SigmaAccumulator();
                sigmaBatch[minute] = acc;
            }

            acc.SumGold += goldDiff;
            acc.SumGoldSq += goldDiff * goldDiff;
            acc.SumDamage += damageDiff;
            acc.SumDamageSq += damageDiff * damageDiff;
            acc.Count++;
        }
    }

    private static void AccumulateCurve(
        Dictionary<CurveKey, CurveAccumulator> curve,
        ParticipantRow p1,
        string patch,
        Dictionary<int, DiffMinute> series)
    {
        foreach (var (minute, diff) in series)
        {
            var key = new CurveKey(p1.ChampionId, p1.TeamPosition, patch, p1.EloBracket, minute);
            if (!curve.TryGetValue(key, out var acc))
            {
                acc = new CurveAccumulator();
                curve[key] = acc;
            }

            acc.Games++;
            acc.GoldDiff += diff.GoldDiff;
            acc.DamageDiff += diff.DamageDiff;
        }
    }

    private static void AccumulateEvents(
        Dictionary<EventKey, EventAccumulator> events,
        ParticipantRow p1,
        int opponentChampionId,
        string patch,
        BuildScope build,
        Dictionary<int, DiffMinute> series,
        IReadOnlyDictionary<int, (double Gold, double Damage)> sigmaByMinute,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadata)
    {
        double? Power(int minute)
        {
            if (!series.TryGetValue(minute, out var diff) || !sigmaByMinute.TryGetValue(minute, out var sigma))
            {
                return null;
            }

            double power = 0;
            var contributed = false;
            if (sigma.Gold > 0) { power += 0.5 * diff.GoldDiff / sigma.Gold; contributed = true; }
            if (sigma.Damage > 0) { power += 0.5 * diff.DamageDiff / sigma.Damage; contributed = true; }
            return contributed ? power : null;
        }

        double? Spike(int eventMinute)
        {
            var before = Power(eventMinute - SpikeWindowMinutes);
            var at = Power(eventMinute);
            var after = Power(eventMinute + SpikeWindowMinutes);
            if (before is null || at is null || after is null)
            {
                return null;
            }

            var slopeBefore = (at.Value - before.Value) / SpikeWindowMinutes;
            var slopeAfter = (after.Value - at.Value) / SpikeWindowMinutes;
            return slopeAfter - slopeBefore;
        }

        void Add(string type, int refId, double spike, int minute)
        {
            var key = new EventKey(
                p1.ChampionId, p1.TeamPosition, patch, p1.EloBracket,
                build.FirstItemId, build.KeystoneId, opponentChampionId, type, refId);
            if (!events.TryGetValue(key, out var acc))
            {
                acc = new EventAccumulator();
                events[key] = acc;
            }

            acc.SumSpike += spike;
            acc.SumMinute += minute;
            acc.Games++;
        }

        // Level milestones: first minute (in the shared series) the champion reached
        // the level, then the slope-change spike around it.
        foreach (var milestone in LevelMilestones)
        {
            int? reached = null;
            foreach (var (minute, diff) in series)
            {
                if (diff.ChampionLevel >= milestone && (reached is null || minute < reached))
                {
                    reached = minute;
                }
            }

            if (reached is not null && Spike(reached.Value) is { } levelSpike)
            {
                Add("level", milestone, levelSpike, reached.Value);
            }
        }

        // Item completions: the first purchase of each genuinely completed item.
        // Completion comes from the patch's item metadata, not from the final
        // inventory — a component that never got upgraded is not a spike, and an
        // item completed then sold still is one.
        //
        // Eligibility is FinalBuildResolver's, shared rather than restated (#1021).
        // The old local test was `IsFinalItem && !IsBootsItem`, but IsFinalItem only
        // means "nothing builds out of this", which is equally true of potions,
        // control wards, trinkets, Doran's and support-quest items — all of which
        // were being folded and rendered as power spikes. Ids are mapped through
        // GetDisplayedBuildItemId for the same reason the build path is: a
        // transform item has to be named the way the dim build tables name it, or
        // the event cannot be matched to the build it belongs to.
        var completions = p1.ItemEvents
            .Where(e => e.ItemId > 0
                && e.EventType.Equals("ITEM_PURCHASED", StringComparison.OrdinalIgnoreCase)
                && itemMetadata.TryGetValue(e.ItemId, out var meta)
                && FinalBuildResolver.IsEligibleFinalBuildItem(meta))
            .GroupBy(e => FinalBuildResolver.GetDisplayedBuildItemId(itemMetadata[e.ItemId]))
            .Select(g => new { ItemId = g.Key, FirstMs = g.Min(e => e.TimestampMs) });

        foreach (var completion in completions)
        {
            var eventMinute = (int)Math.Round(completion.FirstMs / 60_000.0);
            if (Spike(eventMinute) is { } itemSpike)
            {
                Add("item", completion.ItemId, itemSpike, eventMinute);
            }
        }
    }

    /// <summary>
    /// The primary keystone of each participant, i.e. the first <c>primaryStyle</c>
    /// perk selection. Mirrors <c>ChampionPatternSourceRowReader.HydratePerkSelectionsAsync</c>
    /// so the resolved keystone matches the one the builds read groups tabs by.
    /// </summary>
    private static async Task<Dictionary<(string MatchId, int ParticipantId), int>> LoadKeystonesAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        CancellationToken ct)
    {
        var perkRows = await (
            from selection in db.ParticipantPerkSelections.AsNoTracking()
            join catalog in db.PerkSelectionCatalogs.AsNoTracking()
                on selection.PerkSelectionCatalogId equals catalog.Id
            where matchIds.Contains(selection.MatchId)
                // Case-insensitive to match ChampionPatternSourceRowReader, which
                // compares with OrdinalIgnoreCase in memory. A plain `==` would be
                // case-sensitive in Postgres, and a casing change from Riot would
                // silently resolve no keystone at all — dropping every spike while
                // the build tabs kept rendering. No wildcards in the pattern, so
                // there is nothing to escape.
                && EF.Functions.ILike(catalog.StyleDescription, "primaryStyle")
                && catalog.SelectionIndex == 0
            select new { selection.MatchId, selection.ParticipantId, catalog.PerkId })
            .ToListAsync(ct);

        // A malformed match could carry more than one index-0 primary selection;
        // take the first rather than throwing on a duplicate key.
        return perkRows
            .GroupBy(row => (row.MatchId, row.ParticipantId))
            .ToDictionary(group => group.Key, group => group.First().PerkId);
    }

    private static async Task<IReadOnlyDictionary<int, (double Gold, double Damage)>> MergeSigmaAsync(
        TrueMainDbContext db,
        int queueId,
        IReadOnlyDictionary<int, SigmaAccumulator> sigmaBatch,
        CancellationToken ct)
    {
        var existing = await db.PowerspikeSigmaStats
            .AsNoTracking()
            .Where(s => s.QueueId == queueId)
            .ToDictionaryAsync(s => s.IntervalMinute, ct);

        var minutes = existing.Keys.Union(sigmaBatch.Keys);
        var merged = new Dictionary<int, (double Gold, double Damage)>();

        foreach (var minute in minutes)
        {
            double sumGold = 0, sumGoldSq = 0, sumDamage = 0, sumDamageSq = 0;
            long count = 0;

            if (existing.TryGetValue(minute, out var e))
            {
                sumGold = e.SumGoldDiff;
                sumGoldSq = e.SumGoldDiffSq;
                sumDamage = e.SumDamageDiff;
                sumDamageSq = e.SumDamageDiffSq;
                count = e.SampleCount;
            }

            if (sigmaBatch.TryGetValue(minute, out var b))
            {
                sumGold += b.SumGold;
                sumGoldSq += b.SumGoldSq;
                sumDamage += b.SumDamage;
                sumDamageSq += b.SumDamageSq;
                count += b.Count;
            }

            merged[minute] = (SampleStdDev(sumGoldSq, sumGold, count), SampleStdDev(sumDamageSq, sumDamage, count));
        }

        return merged;
    }

    // STDDEV_SAMP: sqrt((Σx² − (Σx)²/n) / (n − 1)), clamped against fp noise.
    private static double SampleStdDev(double sumSq, double sum, long count)
    {
        if (count < 2)
        {
            return 0;
        }

        var variance = (sumSq - sum * sum / count) / (count - 1);
        return variance > 0 ? Math.Sqrt(variance) : 0;
    }

    private static async Task UpsertSigmaAsync(
        TrueMainDbContext db,
        int queueId,
        IReadOnlyDictionary<int, SigmaAccumulator> sigmaBatch,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (sigmaBatch.Count == 0)
        {
            return;
        }

        var rows = sigmaBatch.OrderBy(kv => kv.Key).ToList();
        const string sql = """
            INSERT INTO powerspike_sigma_stats
                ("Id", "QueueId", "IntervalMinute", "SumGoldDiff", "SumGoldDiffSq",
                 "SumDamageDiff", "SumDamageDiffSq", "SampleCount", "AggregatedAtUtc")
            SELECT gen_random_uuid(), @queueId, t.minute, t.sum_gold, t.sum_gold_sq,
                   t.sum_damage, t.sum_damage_sq, t.count, @aggAt
            FROM unnest(@minutes::integer[], @sumGold::double precision[], @sumGoldSq::double precision[],
                        @sumDamage::double precision[], @sumDamageSq::double precision[], @count::bigint[])
                AS t(minute, sum_gold, sum_gold_sq, sum_damage, sum_damage_sq, count)
            ON CONFLICT ("QueueId", "IntervalMinute") DO UPDATE SET
                "SumGoldDiff" = powerspike_sigma_stats."SumGoldDiff" + EXCLUDED."SumGoldDiff",
                "SumGoldDiffSq" = powerspike_sigma_stats."SumGoldDiffSq" + EXCLUDED."SumGoldDiffSq",
                "SumDamageDiff" = powerspike_sigma_stats."SumDamageDiff" + EXCLUDED."SumDamageDiff",
                "SumDamageDiffSq" = powerspike_sigma_stats."SumDamageDiffSq" + EXCLUDED."SumDamageDiffSq",
                "SampleCount" = powerspike_sigma_stats."SampleCount" + EXCLUDED."SampleCount",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("queueId", queueId),
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("minutes", rows.Select(r => r.Key).ToArray()),
                new NpgsqlParameter("sumGold", rows.Select(r => r.Value.SumGold).ToArray()),
                new NpgsqlParameter("sumGoldSq", rows.Select(r => r.Value.SumGoldSq).ToArray()),
                new NpgsqlParameter("sumDamage", rows.Select(r => r.Value.SumDamage).ToArray()),
                new NpgsqlParameter("sumDamageSq", rows.Select(r => r.Value.SumDamageSq).ToArray()),
                new NpgsqlParameter("count", rows.Select(r => r.Value.Count).ToArray())
            ],
            ct);
    }

    private static async Task UpsertCurveAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<CurveKey, CurveAccumulator> curve,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (curve.Count == 0)
        {
            return;
        }

        var rows = curve.ToList();
        const string sql = """
            INSERT INTO champion_powerspike_curve_stats
                ("Id", "ChampionId", "TeamPosition", "Patch", "elo_bracket",
                 "IntervalMinute", "Games", "TotalGoldDiff", "TotalDamageDiff", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.patch, t.elo,
                   t.minute, t.games, t.gold, t.damage, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @elos::text[],
                        @minutes::integer[], @games::integer[], @gold::bigint[], @damage::bigint[])
                AS t(champ, pos, patch, elo, minute, games, gold, damage)
            ON CONFLICT ("ChampionId", "TeamPosition", "Patch", "elo_bracket", "IntervalMinute") DO UPDATE SET
                "Games" = champion_powerspike_curve_stats."Games" + EXCLUDED."Games",
                "TotalGoldDiff" = champion_powerspike_curve_stats."TotalGoldDiff" + EXCLUDED."TotalGoldDiff",
                "TotalDamageDiff" = champion_powerspike_curve_stats."TotalDamageDiff" + EXCLUDED."TotalDamageDiff",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.TeamPosition).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("elos", rows.Select(r => r.Key.EloBracket).ToArray()),
                new NpgsqlParameter("minutes", rows.Select(r => r.Key.IntervalMinute).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                new NpgsqlParameter("gold", rows.Select(r => r.Value.GoldDiff).ToArray()),
                new NpgsqlParameter("damage", rows.Select(r => r.Value.DamageDiff).ToArray())
            ],
            ct);
    }

    private static async Task UpsertEventsAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<EventKey, EventAccumulator> events,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return;
        }

        var rows = events.ToList();
        const string sql = """
            INSERT INTO champion_powerspike_event_stats
                ("Id", "ChampionId", "TeamPosition", "Patch", "elo_bracket",
                 "BuildFirstItemId", "BuildKeystoneId", "OpponentChampionId",
                 "EventType", "RefId", "SumSpike", "SumMinute", "Games", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.patch, t.elo,
                   t.build_item, t.build_keystone, t.opponent,
                   t.type, t.ref_id, t.sum_spike, t.sum_minute, t.games, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @elos::text[],
                        @buildItems::integer[], @buildKeystones::integer[], @opponents::integer[],
                        @types::text[], @refIds::integer[], @sumSpike::double precision[],
                        @sumMinute::double precision[], @games::integer[])
                AS t(champ, pos, patch, elo, build_item, build_keystone, opponent,
                     type, ref_id, sum_spike, sum_minute, games)
            ON CONFLICT ("ChampionId", "TeamPosition", "Patch", "elo_bracket",
                         "BuildFirstItemId", "BuildKeystoneId", "OpponentChampionId",
                         "EventType", "RefId") DO UPDATE SET
                "SumSpike" = champion_powerspike_event_stats."SumSpike" + EXCLUDED."SumSpike",
                "SumMinute" = champion_powerspike_event_stats."SumMinute" + EXCLUDED."SumMinute",
                "Games" = champion_powerspike_event_stats."Games" + EXCLUDED."Games",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.TeamPosition).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("elos", rows.Select(r => r.Key.EloBracket).ToArray()),
                new NpgsqlParameter("buildItems", rows.Select(r => r.Key.BuildFirstItemId).ToArray()),
                new NpgsqlParameter("buildKeystones", rows.Select(r => r.Key.BuildKeystoneId).ToArray()),
                new NpgsqlParameter("opponents", rows.Select(r => r.Key.OpponentChampionId).ToArray()),
                new NpgsqlParameter("types", rows.Select(r => r.Key.EventType).ToArray()),
                new NpgsqlParameter("refIds", rows.Select(r => r.Key.RefId).ToArray()),
                new NpgsqlParameter("sumSpike", rows.Select(r => r.Value.SumSpike).ToArray()),
                new NpgsqlParameter("sumMinute", rows.Select(r => r.Value.SumMinute).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray())
            ],
            ct);
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        string EloBracket,
        bool Tracked,
        int[] FinalItems,
        List<ItemEvent> ItemEvents);

    private readonly record struct ParticipantMinute(int Gold, int Damage, int Level);

    private readonly record struct DiffMinute(long GoldDiff, long DamageDiff, int ChampionLevel);

    private readonly record struct CurveKey(int ChampionId, string TeamPosition, string Patch, string EloBracket, int IntervalMinute);

    private readonly record struct EventKey(
        int ChampionId,
        string TeamPosition,
        string Patch,
        string EloBracket,
        int BuildFirstItemId,
        int BuildKeystoneId,
        int OpponentChampionId,
        string EventType,
        int RefId);

    private readonly record struct BuildScope(int FirstItemId, int KeystoneId);

    private sealed class SigmaAccumulator
    {
        public double SumGold;
        public double SumGoldSq;
        public double SumDamage;
        public double SumDamageSq;
        public long Count;
    }

    private sealed class CurveAccumulator
    {
        public int Games;
        public long GoldDiff;
        public long DamageDiff;
    }

    private sealed class EventAccumulator
    {
        public double SumSpike;
        public double SumMinute;
        public int Games;
    }
}
