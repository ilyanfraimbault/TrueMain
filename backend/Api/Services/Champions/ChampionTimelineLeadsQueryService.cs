using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Average lead vs the lane opponent at each minute mark (5/10/15/20/30) for a
/// champion at a position. Served from the pre-aggregated
/// <c>champion_timeline_lead_stats</c> table (#606): per (patch, interval) rows
/// carry the additive per-game diff totals and a game count, which this read folds
/// to the requested patch scope, divides by games for the average, and floors on
/// the merged total. Replaces the per-request triple self-join over the raw
/// snapshot rows.
/// </summary>
public sealed class ChampionTimelineLeadsQueryService(
    TrueMainDbContext db,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionTimelineLeadsQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<ChampionTimelineLeadsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        var cacheKey = $"champions:timeline-leads:{championId}:{position}:{normalizedPatch ?? "all"}";
        if (cache.TryGetValue<ChampionTimelineLeadsResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        // Reuses the matchup sample floor deliberately: timeline leads are drawn
        // from the same champion-aggregate population. Extract a dedicated
        // MinTimelineGames only if the two ever need independent tuning.
        var minGames = championsOptions.Value.MinMatchupGames;

        // Rows are stored per (patch, interval) with no floor. Fold to the
        // requested scope — one patch, or every patch summed — divide the diff
        // totals by games for the average, and floor on the merged total so the
        // all-patches view floors on the real total, not on any single patch.
        var query = db.ChampionTimelineLeadStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId && s.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }

        var rows = await query
            .GroupBy(s => s.IntervalMinute)
            .Select(g => new
            {
                IntervalMinute = g.Key,
                Games = g.Sum(x => x.Games),
                GoldDiff = g.Sum(x => x.TotalGoldDiff),
                CsDiff = g.Sum(x => x.TotalCsDiff),
                KillsDiff = g.Sum(x => x.TotalKillsDiff),
                LevelDiff = g.Sum(x => x.TotalLevelDiff),
                XpDiff = g.Sum(x => x.TotalXpDiff),
                DamageDiff = g.Sum(x => x.TotalDamageDiff),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        var intervals = rows
            .OrderBy(x => x.IntervalMinute)
            .Select(x => new ChampionTimelineLeadEntry
            {
                IntervalMinute = x.IntervalMinute,
                Games = x.Games,
                GoldDiff = (double)x.GoldDiff / x.Games,
                CsDiff = (double)x.CsDiff / x.Games,
                KillsDiff = (double)x.KillsDiff / x.Games,
                LevelDiff = (double)x.LevelDiff / x.Games,
                XpDiff = (double)x.XpDiff / x.Games,
                DamageDiff = (double)x.DamageDiff / x.Games,
            })
            .ToList();

        var response = new ChampionTimelineLeadsResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Intervals = intervals
        };

        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return response;
    }
}
