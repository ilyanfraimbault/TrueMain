using Core.Lol.Ranking;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Progression;

public interface IChampionScalingQueryService
{
    Task<ChampionScalingResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct);
}

/// <summary>
/// Win rate bucketed by game duration for a champion at a position, plus a single
/// scaling index (win rate of the longest qualifying bucket minus the shortest).
/// Counts the champion's cohort (<see cref="ChampionCohort"/>) — the population of the
/// header and of every other panel on the page — read from <c>champion_scaling_stats</c>,
/// which <c>ChampionScalingAggregationProcess</c> folds match by match (#1368). It used to
/// fold <c>match_participants</c> live on every request, 30–45 s cold for a popular
/// champion; the aggregate holds the same games and wins per bucket, so the read only sums
/// the rows of the requested patch and bands and floors each bucket on the total.
/// </summary>
public sealed class ChampionScalingQueryService(
    TrueMainDbContext db,
    IOptions<ChampionsListOptions> championsOptions,
    IChampionReadCache cache)
    : IChampionScalingQueryService
{
    private static readonly string[] BucketLabels = ["<20m", "20-25m", "25-30m", "30-35m", "35m+"];

    public Task<ChampionScalingResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = PatchFilter.Normalize(patch);

        // The key carries the bracket so each band caches separately; the aggregation
        // version is stamped on by the cache itself.
        var bracketToken = EloBracket.ResolveToken(eloBracket);
        var cacheKey = $"champions:scaling:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";

        return cache.GetOrComputeAsync(
            cacheKey,
            token => ComputeAsync(championId, position, normalizedPatch, eloBracket, token),
            ct);
    }

    private async Task<ChampionScalingResponse> ComputeAsync(
        int championId,
        string position,
        string? normalizedPatch,
        string? eloBracket,
        CancellationToken ct)
    {
        // Resolve the elo filter to its bands (null = ALL, every stored band summed).
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);

        // Shares the matchup sample floor: a scaling bucket needs enough games for
        // its win rate to mean anything, same threshold the sibling reads use.
        var minGames = championsOptions.Value.MinMatchupGames;

        var stats = db.ChampionScalingStats
            .AsNoTracking()
            .Where(stat => stat.ChampionId == championId && stat.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            stats = stats.Where(stat => stat.Patch == normalizedPatch);
        }

        if (bands is not null)
        {
            stats = stats.Where(stat => bands.Contains(stat.EloBracket));
        }

        // Sum the stored rows per bucket and drop thin buckets on the merged total —
        // one SQL round-trip over a handful of rows.
        var rows = await stats
            .GroupBy(stat => stat.Bucket)
            .Select(g => new { Bucket = g.Key, Games = g.Sum(x => x.Games), Wins = g.Sum(x => x.Wins) })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        var buckets = rows
            .OrderBy(x => x.Bucket)
            .Select(x => new ChampionScalingBucket
            {
                Bucket = x.Bucket,
                Label = BucketLabels[x.Bucket],
                Games = x.Games,
                WinRate = (double)x.Wins / x.Games
            })
            .ToList();

        // Needs both ends to be meaningful: the win-rate gap from the shortest to
        // the longest qualifying bucket. Positive = the champion scales late.
        double? scalingIndex = buckets.Count >= 2
            ? buckets[^1].WinRate - buckets[0].WinRate
            : null;

        var response = new ChampionScalingResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Buckets = buckets,
            ScalingIndex = scalingIndex
        };

        return response;
    }
}
