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
/// view would be permanently empty (#775). Item events are intersected with the
/// champion's dominant aggregated build for display. Correlational, not causal:
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
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause); the cache
        // key carries the bracket so each band caches separately. The global
        // per-minute sigma stays unfiltered — it is just a normalising scale.
        var bands = EloBracket.ResolveFilter(eloBracket);
        var bracketToken = EloBracket.ResolveToken(eloBracket);

        var cacheKey = $"champions:powerspikes:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";
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

        var (curve, powerByMinute) = await BuildCurveAsync(
            championId, position, normalizedPatch, bands, minGames, sigmaByMinute, ct);
        var events = await BuildEventsAsync(
            championId, position, queueId, normalizedPatch, bands, minGames, powerByMinute, ct);

        var response = new ChampionPowerspikesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Curve = curve,
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
    // event), divide by games, subtract the population's baseline curvature at the
    // event's mean minute, and keep item events only if they belong to the dominant
    // build. Ordered by descending magnitude.
    private async Task<List<ChampionPowerspikeEvent>> BuildEventsAsync(
        int championId,
        string position,
        int queueId,
        string? normalizedPatch,
        IReadOnlyList<string>? bands,
        int minGames,
        IReadOnlyDictionary<int, double> powerByMinute,
        CancellationToken ct)
    {
        var query = db.ChampionPowerspikeEventStats
            .AsNoTracking()
            .Where(e => e.ChampionId == championId && e.TeamPosition == position);

        if (normalizedPatch is not null)
        {
            query = query.Where(e => e.Patch == normalizedPatch);
        }

        if (bands is not null)
        {
            query = query.Where(e => bands.Contains(e.EloBracket));
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

        // Tiny result set (a handful of events per slice), so the games floor is
        // applied in memory rather than as a translated HAVING clause.
        var rows = grouped.Where(g => g.Games >= minGames).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        // Item spikes are only displayed for the champion's dominant build items.
        var hasItemRows = rows.Any(r => r.EventType == "item");
        var coreItems = hasItemRows
            ? (await LoadDominantBuildItemsAsync(championId, position, queueId, normalizedPatch, ct)).ToHashSet()
            : [];

        return rows
            .Where(r => r.EventType != "item" || coreItems.Contains(r.RefId))
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
            .OrderByDescending(e => e.SpikeMagnitude)
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

    // The completed items of the dominant build for the slice: pick the build id
    // with the most games across the matching aggregate scopes, then read its
    // non-empty item slots in order.
    private async Task<IReadOnlyList<int>> LoadDominantBuildItemsAsync(
        int championId,
        string position,
        int queueId,
        string? normalizedPatch,
        CancellationToken ct)
    {
        // Aggregate scopes store the normalized major.minor patch ("16.14"), not
        // the raw Riot build the matches carry — equality, not the LIKE prefix.
        var topBuildId = await (
                from scope in db.ChampionAggregateScopes.AsNoTracking()
                where scope.ChampionId == championId
                    && scope.Position == position
                    && scope.QueueId == queueId
                    && (normalizedPatch == null || scope.GameVersion == normalizedPatch)
                join pattern in db.ChampionAggregatePatterns.AsNoTracking() on scope.Id equals pattern.ScopeId
                group pattern by pattern.BuildId into g
                orderby g.Sum(p => p.Games) descending
                select g.Key)
            .FirstOrDefaultAsync(ct);

        if (topBuildId == Guid.Empty)
        {
            return [];
        }

        var build = await db.ChampionDimBuilds
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == topBuildId, ct);
        if (build is null)
        {
            return [];
        }

        // Item slots in build order, zeros (empty slots) dropped, de-duplicated.
        int[] slots =
        [
            build.BuildItem0, build.BuildItem1, build.BuildItem2, build.BuildItem3,
            build.BuildItem4, build.BuildItem5, build.BuildItem6
        ];
        return slots.Where(id => id > 0).Distinct().ToList();
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
