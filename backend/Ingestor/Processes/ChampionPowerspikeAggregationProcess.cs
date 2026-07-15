using Core.Lol.Items;
using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Ingestor.Options;
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
/// already-correlational feature). Item spikes are keyed by the participant's own
/// completed items (final inventory minus boots), independent of the dominant build,
/// so no sample is lost when that build shifts — the read intersects with the
/// dominant build for display.
/// </summary>
public sealed class ChampionPowerspikeAggregationProcess(
    ILogger<ChampionPowerspikeAggregationProcess> logger,
    IOptions<PowerspikeAggregationOptions> options,
    IOptions<MainAnalysisOptions> analysisOptions,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private static readonly string[] CanonicalPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    private static readonly int[] LevelMilestones = [6, 11, 16];

    // Half-window (minutes) each side of an event for the slope-change spike.
    // Mirrors ChampionPowerspikesQueryService.
    private const int SpikeWindowMinutes = 3;

    // Boots are a separate pattern dimension and never appear in a dominant build's
    // BuildItem slots, so their item spikes could never be displayed — exclude them
    // up front to keep the event table lean.
    private static readonly IReadOnlySet<int> ExcludedItemIds =
        new HashSet<int>(LolItemIds.TierTwoBoots.All) { LolItemIds.BootsOfSpeed };

    public string Name => "ChampionPowerspikeAggregation";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var batches = 0;

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

        return new { matches = processedMatches, batches };
    }

    private static async Task ProcessBatchAsync(
        TrueMainDbContext db,
        int queueId,
        List<string> matchIds,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var patchByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion })
            .ToDictionaryAsync(m => m.Id, m => PatchVersion.Normalize(m.GameVersion), ct);

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
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

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
                AccumulateEvents(events, p1, patch, series, sigmaByMinute);
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
        string patch,
        Dictionary<int, DiffMinute> series,
        IReadOnlyDictionary<int, (double Gold, double Damage)> sigmaByMinute)
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
            var key = new EventKey(p1.ChampionId, p1.TeamPosition, patch, p1.EloBracket, type, refId);
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
                if (diff.Level >= milestone && (reached is null || minute < reached))
                {
                    reached = minute;
                }
            }

            if (reached is not null && Spike(reached.Value) is { } levelSpike)
            {
                Add("level", milestone, levelSpike, reached.Value);
            }
        }

        // Item completions: first purchase minute of each of the participant's own
        // completed (final-inventory, non-boots) items.
        foreach (var itemId in p1.FinalItems.Where(id => id > 0 && !ExcludedItemIds.Contains(id)).Distinct())
        {
            var firstMs = p1.ItemEvents
                .Where(e => e.ItemId == itemId
                    && e.EventType.Equals("ITEM_PURCHASED", StringComparison.OrdinalIgnoreCase))
                .Select(e => (int?)e.TimestampMs)
                .DefaultIfEmpty(null)
                .Min();
            if (firstMs is null)
            {
                continue;
            }

            var eventMinute = (int)Math.Round(firstMs.Value / 60_000.0);
            if (Spike(eventMinute) is { } itemSpike)
            {
                Add("item", itemId, itemSpike, eventMinute);
            }
        }
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
                 "EventType", "RefId", "SumSpike", "SumMinute", "Games", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.patch, t.elo,
                   t.type, t.ref_id, t.sum_spike, t.sum_minute, t.games, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @elos::text[],
                        @types::text[], @refIds::integer[], @sumSpike::double precision[],
                        @sumMinute::double precision[], @games::integer[])
                AS t(champ, pos, patch, elo, type, ref_id, sum_spike, sum_minute, games)
            ON CONFLICT ("ChampionId", "TeamPosition", "Patch", "elo_bracket", "EventType", "RefId") DO UPDATE SET
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

    private readonly record struct DiffMinute(long GoldDiff, long DamageDiff, int Level);

    private readonly record struct CurveKey(int ChampionId, string TeamPosition, string Patch, string EloBracket, int IntervalMinute);

    private readonly record struct EventKey(int ChampionId, string TeamPosition, string Patch, string EloBracket, string EventType, int RefId);

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
