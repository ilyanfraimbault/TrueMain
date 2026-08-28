using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Builds the champion power curve and its event spikes from the pre-aggregated
/// powerspike stats (#694) — no longer self-joining the dense per-minute
/// <see cref="Data.Entities.MatchParticipantTimelineSnapshot"/> grid (which is then
/// prunable down to the canonical marks).
///
/// The curve is the mean opponent-relative power per minute, where power blends the
/// gold lead and the damage lead, each normalized by the global per-minute spread so
/// the two are comparable: <c>P(m) = 0.5·goldDiff/σ_gold(m) + 0.5·dmgDiff/σ_dmg(m)</c>.
/// Because σ(m) is fixed per minute, the mean over games is linear in the totals, so
/// the read folds <c>champion_powerspike_curve_stats</c> to the requested scope and
/// divides the summed gold/damage lead by the summed game count. σ(m) is recovered
/// from the running sums in <c>powerspike_sigma_stats</c>.
///
/// A spike is the slope-change of that power around an event — a completed build item
/// or a level milestone (6/11/16) — computed per game at aggregation time and kept as
/// additive sums in <c>champion_powerspike_event_stats</c>; the read divides
/// <c>SumSpike</c>/<c>SumMinute</c> by the game count and then subtracts the ambient
/// curvature the mean curve already shows at the event's minute. That baseline
/// subtraction removes the lead curve's global concavity — leads decelerate over time,
/// so the raw slope-change is negative for nearly every event and the "clear spike"
/// view would be permanently empty (#775). It is the reason the curve aggregate is
/// still read even though the curve itself is no longer rendered (#890).
///
/// Events are scoped to one core build and, since #957, to one lane opponent, so the
/// champion page's matchup filter re-slices this section like every other. Only the
/// events move: the mean curve the baseline is subtracted from stays champion-wide on
/// purpose. It is a normaliser for the global concavity of lead curves, and recomputing
/// it on a 4-game matchup would replace that steady correction with noise — worse, it
/// would subtract the matchup's own signal from itself.
///
/// A champion built two ways yields two independent sets of item spikes instead of
/// one blend. Correlational, not causal:
/// a champion completes an item earlier partly because it is already ahead; the
/// opponent-relative + slope-change framing dampens that but does not remove it.
/// Same queue / patch / tracked-account population as the sibling reads.
/// </summary>
public sealed class ChampionPowerspikesQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionPowerspikesQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // Half-window (minutes) each side of an event for the slope-change spike.
    // Mirrors ChampionPowerspikeAggregationProcess.
    private const int SpikeWindowMinutes = 3;

    public async Task<ChampionPowerspikesResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        int buildFirstItemId,
        int buildKeystoneId,
        int? opponentChampionId,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause); the cache
        // key carries the bracket so each band caches separately. The global
        // per-minute sigma stays unfiltered — it is just a normalising scale.
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var bracketToken = EloBracket.ResolveToken(eloBracket);

        var opponent = opponentChampionId is > 0 ? opponentChampionId.Value : (int?)null;

        var cacheKey = $"champions:powerspikes:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}"
            + $":{buildFirstItemId}:{buildKeystoneId}:{opponent?.ToString() ?? "any"}";
        if (cache.TryGetValue<ChampionPowerspikesResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var minGames = championsOptions.Value.MinMatchupGames;

        var empty = new ChampionPowerspikesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch
        };

        // Global per-minute spread σ(m), recovered from the running sums. It is a
        // queue-wide normalising scale, not champion- or patch-scoped.
        var sigmaByMinute = await db.PowerspikeSigmaStats
            .AsNoTracking()
            .Where(s => s.QueueId == queueId)
            .Select(s => new { s.IntervalMinute, s.SumGoldDiff, s.SumGoldDiffSq, s.SumDamageDiff, s.SumDamageDiffSq, s.SampleCount })
            .ToDictionaryAsync(
                s => s.IntervalMinute,
                s => (
                    Gold: SampleStdDev(s.SumGoldDiffSq, s.SumGoldDiff, s.SampleCount),
                    Damage: SampleStdDev(s.SumDamageDiffSq, s.SumDamageDiff, s.SampleCount)),
                ct);

        if (sigmaByMinute.Count == 0)
        {
            cache.Set(cacheKey, empty, CacheEntry());
            return empty;
        }

        // The curve is no longer rendered (#890) but is still read: its mean-power
        // series is the baseline the event spikes are measured against.
        var (_, powerByMinute) = await BuildCurveAsync(
            championId, position, normalizedPatch, bands, minGames, sigmaByMinute, ct);
        // Which items this build actually builds, in build order. The event rows are
        // keyed on the build but hold every item each game completed, so without this
        // list the read cannot tell a core item from a situational one — and the
        // section was showing items absent from the tab's own core path (#1021).
        var coreItemPath = await ChampionCoreBuildPathResolver.ResolveAsync(
            db, cache, queueId, championId, position, normalizedPatch, bands, bracketToken,
            buildFirstItemId, buildKeystoneId, ct);

        var events = await BuildEventsAsync(
            championId, position, normalizedPatch, bands, minGames,
            buildFirstItemId, buildKeystoneId, opponent, powerByMinute, coreItemPath, ct);

        var response = new ChampionPowerspikesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Events = events
        };

        cache.Set(cacheKey, response, CacheEntry());
        return response;
    }

    // Fold the curve stats to the requested scope (sum totals + games per minute),
    // then divide by games and normalise by σ(m) to recover the mean power point.
    // Returns the displayed curve (games floor applied) plus the full mean-power
    // series keyed by minute — the latter is the baseline the event spikes are
    // measured against (no floor, so the ±window lookups stay populated).
    private async Task<(List<ChampionPowerCurvePoint> Curve, IReadOnlyDictionary<int, double> PowerByMinute)> BuildCurveAsync(
        int championId,
        string position,
        string? normalizedPatch,
        IReadOnlyList<string>? bands,
        int minGames,
        IReadOnlyDictionary<int, (double Gold, double Damage)> sigmaByMinute,
        CancellationToken ct)
    {
        var query = db.ChampionPowerspikeCurveStats
            .AsNoTracking()
            .Where(c => c.ChampionId == championId && c.TeamPosition == position);

        if (normalizedPatch is not null)
        {
            query = query.Where(c => c.Patch == normalizedPatch);
        }

        if (bands is not null)
        {
            query = query.Where(c => bands.Contains(c.EloBracket));
        }

        var rows = await query
            .GroupBy(c => c.IntervalMinute)
            .Select(g => new
            {
                Minute = g.Key,
                Games = g.Sum(x => x.Games),
                GoldDiff = g.Sum(x => x.TotalGoldDiff),
                DamageDiff = g.Sum(x => x.TotalDamageDiff)
            })
            .ToListAsync(ct);

        var curve = new List<ChampionPowerCurvePoint>();
        var powerByMinute = new Dictionary<int, double>();
        foreach (var row in rows.OrderBy(r => r.Minute))
        {
            if (!sigmaByMinute.TryGetValue(row.Minute, out var sigma))
            {
                continue;
            }

            double power = 0;
            var contributed = false;
            if (sigma.Gold > 0) { power += 0.5 * ((double)row.GoldDiff / row.Games) / sigma.Gold; contributed = true; }
            if (sigma.Damage > 0) { power += 0.5 * ((double)row.DamageDiff / row.Games) / sigma.Damage; contributed = true; }
            if (!contributed)
            {
                continue;
            }

            // The baseline series carries every minute with a computable mean power;
            // the displayed curve keeps the games floor so thin minutes stay hidden.
            powerByMinute[row.Minute] = power;
            if (row.Games >= minGames)
            {
                curve.Add(new ChampionPowerCurvePoint { Minute = row.Minute, Power = power, Games = row.Games });
            }
        }

        return (curve, powerByMinute);
    }

    // Fold the event spikes to the requested scope (sum spike/minute + games per
    // event), divide by games and subtract the population's baseline curvature at
    // the event's mean minute.
    //
    // Rows are scoped to one core build (#890), which scopes the *games* but not the
    // *items*: every item a game completed produces a row, so a situational item
    // bought in a minority of the slice's games sits in the table next to the build's
    // own. Ranking by magnitude then let it outrank a core item, and the panel showed
    // items absent from the tab's core path. Item events are therefore intersected
    // with that path and returned in its order (#1021) — the order is the build's,
    // not a ranking and not a chronology. Level events keep their own milestone
    // order; they are not build items and no path applies to them.
    //
    // With an opponent (#957) the same rows are narrowed to the games played against
    // them. Rows carry exactly one opponent each, so the unscoped call keeps summing
    // across all of them and its numbers are unchanged by the extra dimension.
    private async Task<List<ChampionPowerspikeEvent>> BuildEventsAsync(
        int championId,
        string position,
        string? normalizedPatch,
        IReadOnlyList<string>? bands,
        int minGames,
        int buildFirstItemId,
        int buildKeystoneId,
        int? opponentChampionId,
        IReadOnlyDictionary<int, double> powerByMinute,
        IReadOnlyList<int> coreItemPath,
        CancellationToken ct)
    {
        // Rank by position in the build path, so an item's place among the bars is
        // the place it holds in the build. An empty path means the slice has no
        // aggregate rows to derive one from; item spikes are then withheld rather
        // than shown unordered, since "which items are this build's" is exactly what
        // could not be answered.
        // TryAdd, not ToDictionary: an item repeated in the path keeps its earliest
        // slot instead of throwing.
        var pathRankByItemId = new Dictionary<int, int>();
        for (var rank = 0; rank < coreItemPath.Count; rank++)
        {
            pathRankByItemId.TryAdd(coreItemPath[rank], rank);
        }

        var query = db.ChampionPowerspikeEventStats
            .AsNoTracking()
            .Where(e => e.ChampionId == championId
                && e.TeamPosition == position
                && e.BuildFirstItemId == buildFirstItemId
                && e.BuildKeystoneId == buildKeystoneId);

        if (normalizedPatch is not null)
        {
            query = query.Where(e => e.Patch == normalizedPatch);
        }

        if (bands is not null)
        {
            query = query.Where(e => bands.Contains(e.EloBracket));
        }

        if (opponentChampionId is { } opponent)
        {
            // Rows folded before #957 sit at 0 ("opponent not recorded"), as do rows
            // retention has rolled back up once their patch froze. Both are blends of
            // every opponent, so a matchup filter must not match them — it would
            // silently answer with the global slice.
            query = query.Where(e => e.OpponentChampionId == opponent);
        }

        var grouped = await query
            .GroupBy(e => new { e.EventType, e.RefId })
            .Select(g => new
            {
                g.Key.EventType,
                g.Key.RefId,
                Games = g.Sum(x => x.Games),
                SumSpike = g.Sum(x => x.SumSpike),
                SumMinute = g.Sum(x => x.SumMinute)
            })
            .ToListAsync(ct);

        // No games floor on a matchup slice, matching the rest of the matchup-filtered
        // page (decided 2026-07-30, #923): the median champion-vs-opponent pair holds
        // 4 games on a patch, so applying the global floor would empty the section for
        // nearly every matchup. The honest answer to a thin sample is the sample size,
        // which every event already carries.
        var floor = opponentChampionId is null ? minGames : 1;

        // Tiny result set (a handful of events per slice), so the games floor is
        // applied in memory rather than as a translated HAVING clause.
        var rows = grouped.Where(g => g.Games >= floor).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        const string ItemEventType = "item";

        return rows
            .Where(r => r.EventType != ItemEventType || pathRankByItemId.ContainsKey(r.RefId))
            .Select(r =>
            {
                var avgMinute = r.SumMinute / r.Games;
                return new ChampionPowerspikeEvent
                {
                    Type = r.EventType,
                    RefId = r.RefId,
                    AvgMinute = avgMinute,
                    // Excess over the ambient curvature: the raw slope-change minus
                    // what the mean curve does anyway at this minute. Without it the
                    // metric inherits the lead curve's global concavity (leads
                    // decelerate over time), so every event reads negative and the
                    // "clear spike" view is permanently empty.
                    SpikeMagnitude = r.SumSpike / r.Games - BaselineCurvature(powerByMinute, avgMinute),
                    Games = r.Games
                };
            })
            // Items in build order first, then the level milestones in their own
            // order. Magnitude no longer sorts anything: the bar row is read
            // left-to-right as the build, so ranking by size would scramble it.
            .OrderBy(e => e.Type == ItemEventType ? 0 : 1)
            .ThenBy(e => e.Type == ItemEventType ? pathRankByItemId[e.RefId] : e.RefId)
            .ToList();
    }

    // Second difference of the mean power curve around a minute, on the same ±window
    // and scale as the per-game spike: (P(m+w) − 2·P(m) + P(m−w)) / w. Zero when any
    // of the three minutes is missing — the event then keeps its raw slope-change.
    private static double BaselineCurvature(IReadOnlyDictionary<int, double> powerByMinute, double avgMinute)
    {
        var m = (int)Math.Round(avgMinute);
        if (powerByMinute.TryGetValue(m - SpikeWindowMinutes, out var before)
            && powerByMinute.TryGetValue(m, out var at)
            && powerByMinute.TryGetValue(m + SpikeWindowMinutes, out var after))
        {
            return (after - 2 * at + before) / SpikeWindowMinutes;
        }

        return 0;
    }

    // STDDEV_SAMP: sqrt((Σx² − (Σx)²/n) / (n − 1)), clamped against fp noise.
    // Mirrors ChampionPowerspikeAggregationProcess so the read recovers the same σ.
    private static double SampleStdDev(double sumSq, double sum, long count)
    {
        if (count < 2)
        {
            return 0;
        }

        var variance = (sumSq - sum * sum / count) / (count - 1);
        return variance > 0 ? Math.Sqrt(variance) : 0;
    }

    private static MemoryCacheEntryOptions CacheEntry()
        => new() { AbsoluteExpirationRelativeToNow = CacheTtl, Size = 1 };
}
