using Core.Lol.Patches;
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
/// timeline tables — no raw per-minute grid is read. The curve is the mean
/// opponent-relative power per minute, where power blends the gold lead and the
/// damage lead, each normalized by the global per-minute spread so the two are
/// comparable: <c>P(t) = 0.5·goldDiff/σ_gold(t) + 0.5·dmgDiff/σ_dmg(t)</c>. Because
/// σ depends only on the minute, the mean power is
/// <c>0.5·avgGold(t)/σ_gold(t) + 0.5·avgDmg(t)/σ_dmg(t)</c>, so the curve reads the
/// same additive lead totals as the timeline-leads slice (over every minute) and
/// reconstructs σ from the lead-spread variance moments.
///
/// A spike is the curvature of that aggregate curve around an event — the first
/// purchase of a core build item, or a level milestone (6/11/16): the slope of P
/// after the mean event minute minus the slope before, over a ±3 min window.
/// Correlational, not causal: a champion completes an item earlier partly because
/// it is already ahead; the opponent-relative + slope-change framing dampens that
/// but does not remove it.
///
/// Item events are driven by the champion's dominant aggregated build (its
/// completed items), so no item-metadata classification is needed here.
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

    // Half-window (minutes) on each side of an event for the slope-change spike.
    private const int SpikeWindowMinutes = 3;

    private const int MaxMinute = 30;

    private static readonly int[] LevelMilestones = [6, 11, 16];

    public async Task<ChampionPowerspikesResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        var cacheKey = $"champions:powerspikes:{championId}:{position}:{normalizedPatch ?? "all"}";
        if (cache.TryGetValue<ChampionPowerspikesResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";
        var minGames = championsOptions.Value.MinMatchupGames;

        var empty = new ChampionPowerspikesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch
        };

        // Per-minute lead totals for this slice (every minute 1..30), folded across
        // the requested patch scope: totals / games give the mean lead, and the
        // sigma moments give the per-minute spread that normalizes it.
        var leadQuery = db.ChampionTimelineLeadStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId && s.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            leadQuery = leadQuery.Where(s => s.Patch == normalizedPatch);
        }

        var leadRows = await leadQuery
            .GroupBy(s => s.IntervalMinute)
            .Select(g => new
            {
                Minute = g.Key,
                Games = g.Sum(x => x.Games),
                Gold = g.Sum(x => x.TotalGoldDiff),
                Damage = g.Sum(x => x.TotalDamageDiff),
            })
            .ToListAsync(ct);

        if (leadRows.Count == 0)
        {
            cache.Set(cacheKey, empty, CacheEntry(CacheTtl));
            return empty;
        }

        var sigmas = await LoadSigmasAsync(queueId, normalizedPatch, ct);
        var leadByMinute = leadRows.ToDictionary(r => r.Minute);

        // Normalized power at a minute, no floor (the spike windows read this). Null
        // when the minute is missing or its spread is degenerate on both channels.
        double? PowerAt(int minute)
        {
            if (!leadByMinute.TryGetValue(minute, out var lead)
                || lead.Games <= 0
                || !sigmas.TryGetValue(minute, out var sigma))
            {
                return null;
            }

            double power = 0;
            var contributed = false;
            if (sigma.Gold > 0) { power += 0.5 * ((double)lead.Gold / lead.Games) / sigma.Gold; contributed = true; }
            if (sigma.Damage > 0) { power += 0.5 * ((double)lead.Damage / lead.Games) / sigma.Damage; contributed = true; }
            return contributed ? power : null;
        }

        // Curvature of the aggregate curve around a mean event minute.
        double? Spike(double meanMinute)
        {
            var eventMinute = (int)Math.Round(meanMinute, MidpointRounding.AwayFromZero);
            var before = PowerAt(eventMinute - SpikeWindowMinutes);
            var at = PowerAt(eventMinute);
            var after = PowerAt(eventMinute + SpikeWindowMinutes);
            if (before is null || at is null || after is null)
            {
                return null;
            }

            var slopeBefore = (at.Value - before.Value) / SpikeWindowMinutes;
            var slopeAfter = (after.Value - at.Value) / SpikeWindowMinutes;
            return slopeAfter - slopeBefore;
        }

        // Curve: mean power per minute, only minutes above the games floor.
        var curve = new List<ChampionPowerCurvePoint>();
        for (var minute = 1; minute <= MaxMinute; minute++)
        {
            if (!leadByMinute.TryGetValue(minute, out var lead) || lead.Games < minGames)
            {
                continue;
            }

            var power = PowerAt(minute);
            if (power is not null)
            {
                curve.Add(new ChampionPowerCurvePoint
                {
                    Minute = minute,
                    Power = power.Value,
                    Games = lead.Games,
                });
            }
        }

        var events = await BuildEventsAsync(
            championId, position, queueId, normalizedPatch, patchPrefix, minGames, Spike, ct);

        var response = new ChampionPowerspikesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Curve = curve,
            Events = events
                .OrderByDescending(e => e.SpikeMagnitude)
                .ToList()
        };

        cache.Set(cacheKey, response, CacheEntry(CacheTtl));
        return response;
    }

    // Per-minute gold / damage spread reconstructed from the additive variance
    // moments, folded across the patch scope (moments are additive, so summing them
    // is the pooled spread): σ = sqrt((SumSq − Sum²/N) / (N − 1)).
    private async Task<IReadOnlyDictionary<int, (double Gold, double Damage)>> LoadSigmasAsync(
        int queueId,
        string? normalizedPatch,
        CancellationToken ct)
    {
        var query = db.TimelineLeadSigmaMoments
            .AsNoTracking()
            .Where(s => s.QueueId == queueId);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }

        var rows = await query
            .GroupBy(s => s.IntervalMinute)
            .Select(g => new
            {
                Minute = g.Key,
                N = g.Sum(x => x.N),
                SumGold = g.Sum(x => x.SumGold),
                SumSqGold = g.Sum(x => x.SumSqGold),
                SumDmg = g.Sum(x => x.SumDmg),
                SumSqDmg = g.Sum(x => x.SumSqDmg),
            })
            .ToListAsync(ct);

        var sigmas = new Dictionary<int, (double, double)>();
        foreach (var row in rows)
        {
            if (row.N < 2)
            {
                continue;
            }

            sigmas[row.Minute] = (StdDev(row.N, row.SumGold, row.SumSqGold), StdDev(row.N, row.SumDmg, row.SumSqDmg));
        }

        return sigmas;
    }

    private static double StdDev(long n, double sum, double sumSq)
    {
        // Sample variance from moments; clamp tiny negatives from float rounding.
        var variance = (sumSq - sum * sum / n) / (n - 1);
        return variance > 0 ? Math.Sqrt(variance) : 0;
    }

    private async Task<List<ChampionPowerspikeEvent>> BuildEventsAsync(
        int championId,
        string position,
        int queueId,
        string? normalizedPatch,
        string? patchPrefix,
        int minGames,
        Func<double, double?> spike,
        CancellationToken ct)
    {
        var query = db.ChampionPowerspikeEventStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId && s.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }

        var stats = await query
            .GroupBy(s => new { s.EventType, s.RefId })
            .Select(g => new
            {
                g.Key.EventType,
                g.Key.RefId,
                Games = g.Sum(x => x.Games),
                SumMinute = g.Sum(x => x.SumEventMinute),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        if (stats.Count == 0)
        {
            return [];
        }

        // Items are limited to the champion's dominant build; level milestones are
        // always in scope. Everything else (off-build item buys) is dropped.
        var coreItems = (await LoadDominantBuildItemsAsync(
            championId, position, queueId, normalizedPatch, patchPrefix, ct)).ToHashSet();

        var events = new List<ChampionPowerspikeEvent>();
        foreach (var stat in stats)
        {
            var isLevel = string.Equals(stat.EventType, "level", StringComparison.Ordinal);
            if (isLevel)
            {
                if (!LevelMilestones.Contains(stat.RefId))
                {
                    continue;
                }
            }
            else if (!coreItems.Contains(stat.RefId))
            {
                continue;
            }

            var meanMinute = (double)stat.SumMinute / stat.Games;
            var magnitude = spike(meanMinute);
            if (magnitude is null)
            {
                continue;
            }

            events.Add(new ChampionPowerspikeEvent
            {
                Type = stat.EventType,
                RefId = stat.RefId,
                AvgMinute = meanMinute,
                SpikeMagnitude = magnitude.Value,
                Games = stat.Games,
            });
        }

        return events;
    }

    // The completed items of the dominant build for the slice: pick the build id
    // with the most games across the matching aggregate scopes, then read its
    // non-empty item slots in order.
    private async Task<IReadOnlyList<int>> LoadDominantBuildItemsAsync(
        int championId,
        string position,
        int queueId,
        string? normalizedPatch,
        string? patchPrefix,
        CancellationToken ct)
    {
        var topBuildId = await (
                from scope in db.ChampionAggregateScopes.AsNoTracking()
                where scope.ChampionId == championId
                    && scope.Position == position
                    && scope.QueueId == queueId
                    && (normalizedPatch == null || EF.Functions.Like(scope.GameVersion, patchPrefix!))
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

    private static MemoryCacheEntryOptions CacheEntry(TimeSpan ttl)
        => new() { AbsoluteExpirationRelativeToNow = ttl, Size = 1 };
}
