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
/// Builds the champion power curve and its event spikes. The curve is the mean
/// opponent-relative power per minute, where power blends the gold lead and the
/// damage lead, each normalized by the global per-minute spread so the two
/// comparable: <c>P(t) = 0.5·goldDiff/σ_gold(t) + 0.5·dmgDiff/σ_dmg(t)</c>.
///
/// A spike is the acceleration of that power around an event — the completion of
/// a core build item, or a level milestone (6/11/16): the slope of P after the
/// event minus the slope before, over a ±3 min window, averaged across games.
/// Correlational, not causal: a champion completes an item earlier partly
/// because it is already ahead; the opponent-relative + slope-change framing
/// dampens that but does not remove it.
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

    // The global per-minute spread is a slowly-changing population statistic and
    // its query scans broadly, so it gets its own long-lived cache entry.
    private static readonly TimeSpan SigmaCacheTtl = TimeSpan.FromMinutes(30);

    // Half-window (minutes) on each side of an event for the slope-change spike.
    private const int SpikeWindowMinutes = 3;

    private static readonly int[] LevelMilestones = [6, 11, 16];

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
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";
        var minGames = championsOptions.Value.MinMatchupGames;

        var empty = new ChampionPowerspikesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch
        };

        // Per (match, minute): the champion's gold/damage lead over its lane
        // opponent plus the champion's own level. Same opponent pairing as the
        // timeline-leads read, but every minute and carrying Level.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId
                && p1.TeamPosition == position
                && p1.RiotAccountId != null)
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null || EF.Functions.Like(m.GameVersion, patchPrefix!))));

        // Narrow the champion side to the requested elo bands (null = every band).
        if (bands is not null)
        {
            championRows = championRows.Where(p1 => bands.Contains(p1.EloBracket));
        }

        var diffRows = await championRows
            .SelectMany(
                p1 => db.MatchParticipants.Where(p2 =>
                    p2.MatchId == p1.MatchId
                    && p2.TeamPosition == p1.TeamPosition
                    && p2.TeamId != p1.TeamId),
                (p1, p2) => new { p1, p2 })
            .SelectMany(
                pair => db.MatchParticipantTimelineSnapshots.Where(s1 =>
                    s1.MatchId == pair.p1.MatchId && s1.ParticipantId == pair.p1.ParticipantId),
                (pair, s1) => new { pair.p2, s1 })
            .SelectMany(
                x => db.MatchParticipantTimelineSnapshots.Where(s2 =>
                    s2.MatchId == x.p2.MatchId
                    && s2.ParticipantId == x.p2.ParticipantId
                    && s2.IntervalMinute == x.s1.IntervalMinute),
                (x, s2) => new DiffRow(
                    x.s1.MatchId,
                    x.s1.IntervalMinute,
                    x.s1.TotalGold - s2.TotalGold,
                    x.s1.DamageToChampions - s2.DamageToChampions,
                    x.s1.Level))
            .ToListAsync(ct);

        if (diffRows.Count == 0)
        {
            cache.Set(cacheKey, empty, CacheEntry(CacheTtl));
            return empty;
        }

        var sigmas = await GetGlobalSigmasAsync(queueId, ct);

        // Per match: minute -> (lead, level), and minute -> power.
        var byMatch = diffRows
            .GroupBy(r => r.MatchId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.Minute, r => r));

        double? Power(IReadOnlyDictionary<int, DiffRow> series, int minute)
        {
            if (!series.TryGetValue(minute, out var row) || !sigmas.TryGetValue(minute, out var sigma))
            {
                return null;
            }

            double power = 0;
            var contributed = false;
            if (sigma.Gold > 0) { power += 0.5 * row.GoldDiff / sigma.Gold; contributed = true; }
            if (sigma.Damage > 0) { power += 0.5 * row.DmgDiff / sigma.Damage; contributed = true; }
            return contributed ? power : null;
        }

        // Slope-change spike around an event minute on one game's power series.
        double? Spike(IReadOnlyDictionary<int, DiffRow> series, int eventMinute)
        {
            var before = Power(series, eventMinute - SpikeWindowMinutes);
            var at = Power(series, eventMinute);
            var after = Power(series, eventMinute + SpikeWindowMinutes);
            if (before is null || at is null || after is null)
            {
                return null;
            }

            var slopeBefore = (at.Value - before.Value) / SpikeWindowMinutes;
            var slopeAfter = (after.Value - at.Value) / SpikeWindowMinutes;
            return slopeAfter - slopeBefore;
        }

        // Curve: mean power per minute across games (only minutes above the floor).
        var curve = new List<ChampionPowerCurvePoint>();
        for (var minute = 1; minute <= MaxMinute; minute++)
        {
            var powers = byMatch.Values
                .Select(series => Power(series, minute))
                .Where(p => p is not null)
                .Select(p => p!.Value)
                .ToList();
            if (powers.Count >= minGames)
            {
                curve.Add(new ChampionPowerCurvePoint
                {
                    Minute = minute,
                    Power = powers.Average(),
                    Games = powers.Count
                });
            }
        }

        var events = new List<ChampionPowerspikeEvent>();

        // Level milestones: first minute the champion reaches the level, per game.
        foreach (var milestone in LevelMilestones)
        {
            var spikes = new List<double>();
            var minutes = new List<int>();
            foreach (var series in byMatch.Values)
            {
                var reached = series.Values
                    .Where(r => r.Level >= milestone)
                    .Select(r => (int?)r.Minute)
                    .DefaultIfEmpty(null)
                    .Min();
                if (reached is null)
                {
                    continue;
                }

                var spike = Spike(series, reached.Value);
                if (spike is not null)
                {
                    spikes.Add(spike.Value);
                    minutes.Add(reached.Value);
                }
            }

            if (spikes.Count >= minGames)
            {
                events.Add(new ChampionPowerspikeEvent
                {
                    Type = "level",
                    RefId = milestone,
                    AvgMinute = minutes.Average(),
                    SpikeMagnitude = spikes.Average(),
                    Games = spikes.Count
                });
            }
        }

        // Item events: the champion's dominant build's completed items.
        var coreItems = await LoadDominantBuildItemsAsync(championId, position, queueId, normalizedPatch, ct);
        if (coreItems.Count > 0)
        {
            var itemFirstByMatch = await LoadItemFirstPurchasesAsync(
                championId, position, queueId, patchPrefix, coreItems, ct);

            foreach (var itemId in coreItems)
            {
                var spikes = new List<double>();
                var minutes = new List<int>();
                foreach (var (matchId, series) in byMatch)
                {
                    if (!itemFirstByMatch.TryGetValue((matchId, itemId), out var firstMs))
                    {
                        continue;
                    }

                    var eventMinute = (int)Math.Round(firstMs / 60_000.0);
                    var spike = Spike(series, eventMinute);
                    if (spike is not null)
                    {
                        spikes.Add(spike.Value);
                        minutes.Add(eventMinute);
                    }
                }

                if (spikes.Count >= minGames)
                {
                    events.Add(new ChampionPowerspikeEvent
                    {
                        Type = "item",
                        RefId = itemId,
                        AvgMinute = minutes.Average(),
                        SpikeMagnitude = spikes.Average(),
                        Games = spikes.Count
                    });
                }
            }
        }

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

    // Per-minute spread of the gold / damage lead across the whole tracked
    // population, used to make the two comparable. Cached on the queue: it is a
    // global, slowly-changing scale, not per champion.
    private async Task<IReadOnlyDictionary<int, (double Gold, double Damage)>> GetGlobalSigmasAsync(
        int queueId,
        CancellationToken ct)
    {
        var key = $"champions:powerspikes:sigmas:{queueId}";
        if (cache.TryGetValue<IReadOnlyDictionary<int, (double, double)>>(key, out var cachedSigmas)
            && cachedSigmas is not null)
        {
            return cachedSigmas;
        }

        FormattableString sql = $@"
            SELECT s1.""IntervalMinute"" AS ""Minute"",
                   COALESCE(STDDEV_SAMP(s1.""TotalGold"" - s2.""TotalGold""), 0)::double precision AS ""SigmaGold"",
                   COALESCE(STDDEV_SAMP(s1.""DamageToChampions"" - s2.""DamageToChampions""), 0)::double precision AS ""SigmaDmg""
            FROM match_participant_timeline_snapshots s1
            JOIN match_participants mp1 ON mp1.""MatchId"" = s1.""MatchId"" AND mp1.""ParticipantId"" = s1.""ParticipantId""
            JOIN match_participants mp2 ON mp2.""MatchId"" = s1.""MatchId""
                AND mp2.""TeamPosition"" = mp1.""TeamPosition"" AND mp2.""TeamId"" <> mp1.""TeamId""
            JOIN match_participant_timeline_snapshots s2 ON s2.""MatchId"" = s1.""MatchId""
                AND s2.""ParticipantId"" = mp2.""ParticipantId"" AND s2.""IntervalMinute"" = s1.""IntervalMinute""
            JOIN matches m ON m.""Id"" = s1.""MatchId"" AND m.""QueueId"" = {queueId}
            GROUP BY s1.""IntervalMinute""";

        var rows = await db.Database.SqlQuery<SigmaRow>(sql).ToListAsync(ct);
        var sigmas = rows.ToDictionary(r => r.Minute, r => (r.SigmaGold, r.SigmaDmg));

        cache.Set(key, (IReadOnlyDictionary<int, (double, double)>)sigmas, CacheEntry(SigmaCacheTtl));
        return sigmas;
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

    // First ITEM_PURCHASED timestamp of each core item per game, unnested from
    // the participants' ItemEvents jsonb (same source as item-timings).
    private async Task<IReadOnlyDictionary<(string MatchId, int ItemId), int>> LoadItemFirstPurchasesAsync(
        int championId,
        string position,
        int queueId,
        string? patchPrefix,
        IReadOnlyList<int> coreItems,
        CancellationToken ct)
    {
        var coreItemsArray = coreItems.ToArray();

        FormattableString sql = $@"
            SELECT mp.""MatchId"" AS ""MatchId"",
                   e.item_id AS ""ItemId"",
                   MIN(e.ts)::int AS ""FirstMs""
            FROM match_participants mp
            JOIN matches m ON m.""Id"" = mp.""MatchId""
            CROSS JOIN LATERAL (
                SELECT (ev->>'ItemId')::int AS item_id,
                       (ev->>'TimestampMs')::int AS ts
                FROM jsonb_array_elements(mp.""ItemEvents"") ev
                WHERE ev->>'EventType' = 'ITEM_PURCHASED'
                  AND (ev->>'ItemId')::int = ANY({coreItemsArray})
            ) e
            WHERE mp.""ChampionId"" = {championId}
              AND mp.""TeamPosition"" = {position}
              AND mp.""RiotAccountId"" IS NOT NULL
              AND m.""QueueId"" = {queueId}
              AND ({patchPrefix}::text IS NULL OR m.""GameVersion"" LIKE {patchPrefix})
            GROUP BY mp.""MatchId"", e.item_id";

        var rows = await db.Database.SqlQuery<ItemFirstRow>(sql).ToListAsync(ct);
        return rows.ToDictionary(r => (r.MatchId, r.ItemId), r => r.FirstMs);
    }

    private const int MaxMinute = 30;

    private static MemoryCacheEntryOptions CacheEntry(TimeSpan ttl)
        => new() { AbsoluteExpirationRelativeToNow = ttl, Size = 1 };

    private sealed record DiffRow(string MatchId, int Minute, int GoldDiff, int DmgDiff, int Level);

    private sealed record SigmaRow(int Minute, double SigmaGold, double SigmaDmg);

    private sealed record ItemFirstRow(string MatchId, int ItemId, int FirstMs);
}
