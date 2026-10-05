using Core.Lol.Pace;
using Core.Lol.Patches;
using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Benchmarks;

namespace TrueMain.Services.Benchmarks;

public interface IPaceBenchmarkQueryService
{
    /// <summary>The pace benchmark of <paramref name="position"/>, a canonical <c>TeamPosition</c>.</summary>
    Task<PaceBenchmarkResponse> GetAsync(string position, CancellationToken ct);
}

/// <summary>
/// Reads <c>pace_benchmark_stats</c> (#1912) for one position: the bins of the newest
/// <see cref="PatchWindow"/> patches are summed, then each (tier, minute, metric) histogram
/// is read back as quartiles.
/// </summary>
public sealed class PaceBenchmarkQueryService(TrueMainDbContext db, IMemoryCache cache) : IPaceBenchmarkQueryService
{
    /// <summary>
    /// Patches pooled per read. A laner's pace moves little from one patch to the next, and
    /// pooling keeps the lower tiers' cells above the floor early in a patch; three is about
    /// six weeks of games.
    /// </summary>
    internal const int PatchWindow = 3;

    /// <summary>
    /// Samples a (tier, minute) cell needs before its quartiles are served. Under it the
    /// panel shows no comparison rather than a quartile drawn from a handful of games.
    /// </summary>
    internal const int MinSamples = 50;

    /// <summary>The bins only grow with each ingested match; an hour of staleness is invisible in a quartile.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<PaceBenchmarkResponse> GetAsync(string position, CancellationToken ct)
    {
        var cacheKey = (nameof(PaceBenchmarkQueryService), position);
        if (cache.TryGetValue(cacheKey, out PaceBenchmarkResponse? cached) && cached is not null)
        {
            return cached;
        }

        return cache.Store(cacheKey, await ReadAsync(position, ct), CacheTtl);
    }

    private async Task<PaceBenchmarkResponse> ReadAsync(string position, CancellationToken ct)
    {
        var storedPatches = await db.PaceBenchmarkStats
            .AsNoTracking()
            .Where(stat => stat.Position == position)
            .Select(stat => stat.Patch)
            .Distinct()
            .ToListAsync(ct);

        var patches = NewestPatches(storedPatches, PatchWindow);
        if (patches.Count == 0)
        {
            return new PaceBenchmarkResponse { Position = position, MinSamples = MinSamples };
        }

        var bins = await db.PaceBenchmarkStats
            .AsNoTracking()
            .Where(stat => stat.Position == position && patches.Contains(stat.Patch))
            .GroupBy(stat => new { stat.Tier, stat.Minute, stat.Metric, stat.Bucket })
            .Select(group => new PaceBin(group.Key.Tier, group.Key.Minute, group.Key.Metric, group.Key.Bucket, group.Sum(stat => stat.Count)))
            .ToListAsync(ct);

        return new PaceBenchmarkResponse
        {
            Position = position,
            Patches = patches,
            MinSamples = MinSamples,
            Tiers = Build(bins),
        };
    }

    internal static List<string> NewestPatches(IEnumerable<string> stored, int count)
        => stored
            .Select(raw => PatchVersion.TryParse(raw, out var version) ? (Raw: raw, Version: version) : default((string Raw, PatchVersion Version)?))
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .OrderByDescending(entry => entry.Version)
            .Take(count)
            .Select(entry => entry.Raw)
            .ToList();

    internal static List<PaceBenchmarkTierReadModel> Build(IReadOnlyCollection<PaceBin> bins)
    {
        var byTier = bins.ToLookup(bin => bin.Tier, StringComparer.Ordinal);

        return EloBracket.Ladder
            .Where(byTier.Contains)
            .Select(tier => new PaceBenchmarkTierReadModel
            {
                Tier = tier,
                Minutes = byTier[tier]
                    .GroupBy(bin => bin.Minute)
                    .OrderBy(minute => minute.Key)
                    .Select(minute => BuildMinute(minute.Key, minute.ToList()))
                    .ToList(),
            })
            .ToList();
    }

    private static PaceBenchmarkMinuteReadModel BuildMinute(int minute, IReadOnlyCollection<PaceBin> bins)
    {
        var cs = Histogram(bins, PaceMetric.Cs);
        var gold = Histogram(bins, PaceMetric.GoldEarned);

        // Every sampled laner adds one CS bin and one gold bin, so either histogram's total
        // is the sample; the smaller one is taken in case a fold was ever interrupted between them.
        var samples = Math.Min(cs.Sum(bin => bin.Count), gold.Sum(bin => bin.Count));
        var served = samples >= MinSamples;

        return new PaceBenchmarkMinuteReadModel
        {
            Minute = minute,
            Samples = samples,
            Cs = served ? Quartiles(PaceMetric.Cs, cs) : null,
            GoldEarned = served ? Quartiles(PaceMetric.GoldEarned, gold) : null,
        };
    }

    private static List<(int Bucket, long Count)> Histogram(IEnumerable<PaceBin> bins, PaceMetric metric)
        => bins.Where(bin => bin.Metric == metric).Select(bin => (bin.Bucket, bin.Count)).ToList();

    private static PaceQuartilesReadModel? Quartiles(PaceMetric metric, IReadOnlyCollection<(int Bucket, long Count)> histogram)
    {
        var median = PaceHistogram.Percentile(metric, histogram, 0.5);
        if (median is null)
        {
            return null;
        }

        return new PaceQuartilesReadModel
        {
            P25 = Math.Round(PaceHistogram.Percentile(metric, histogram, 0.25)!.Value, 1),
            Median = Math.Round(median.Value, 1),
            P75 = Math.Round(PaceHistogram.Percentile(metric, histogram, 0.75)!.Value, 1),
        };
    }

    internal sealed record PaceBin(string Tier, int Minute, PaceMetric Metric, int Bucket, long Count);
}
