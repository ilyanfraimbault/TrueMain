using Core.Lol.Benchmarks;
using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Benchmarks;

namespace TrueMain.Services.Benchmarks;

public interface IFormBenchmarkQueryService
{
    /// <summary>The form benchmark of <paramref name="position"/>, a canonical <c>TeamPosition</c>.</summary>
    Task<FormBenchmarkResponse> GetAsync(string position, CancellationToken ct);
}

/// <summary>
/// Reads <c>form_benchmark_stats</c> (#2076) for one position: the bins of the newest
/// <see cref="PaceBenchmarkQueryService.PatchWindow"/> patches are summed — the pace
/// benchmark's window, since both are folded from the same games — then each (tier, metric)
/// histogram is read back as quartiles.
/// </summary>
public sealed class FormBenchmarkQueryService(TrueMainDbContext db, IMemoryCache cache) : IFormBenchmarkQueryService
{
    /// <summary>
    /// Games a tier needs before its quartiles are served — the pace benchmark's floor, per
    /// tier rather than per minute.
    /// </summary>
    internal const int MinSamples = PaceBenchmarkQueryService.MinSamples;

    /// <summary>The bins only grow with each ingested match; an hour of staleness is invisible in a median.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<FormBenchmarkResponse> GetAsync(string position, CancellationToken ct)
    {
        var cacheKey = (nameof(FormBenchmarkQueryService), position);
        if (cache.TryGetValue(cacheKey, out FormBenchmarkResponse? cached) && cached is not null)
        {
            return cached;
        }

        return cache.Store(cacheKey, await ReadAsync(position, ct), CacheTtl);
    }

    private async Task<FormBenchmarkResponse> ReadAsync(string position, CancellationToken ct)
    {
        var storedPatches = await db.FormBenchmarkStats
            .AsNoTracking()
            .Where(stat => stat.Position == position)
            .Select(stat => stat.Patch)
            .Distinct()
            .ToListAsync(ct);

        var patches = PaceBenchmarkQueryService.NewestPatches(storedPatches, PaceBenchmarkQueryService.PatchWindow);
        if (patches.Count == 0)
        {
            return new FormBenchmarkResponse { Position = position, MinSamples = MinSamples };
        }

        var bins = await db.FormBenchmarkStats
            .AsNoTracking()
            .Where(stat => stat.Position == position && patches.Contains(stat.Patch))
            .GroupBy(stat => new { stat.Tier, stat.Metric, stat.Bucket })
            .Select(group => new FormBin(group.Key.Tier, group.Key.Metric, group.Key.Bucket, group.Sum(stat => stat.Count)))
            .ToListAsync(ct);

        return new FormBenchmarkResponse
        {
            Position = position,
            Patches = patches,
            MinSamples = MinSamples,
            Tiers = Build(bins),
        };
    }

    internal static List<FormBenchmarkTierReadModel> Build(IReadOnlyCollection<FormBin> bins)
    {
        var byTier = bins.ToLookup(bin => bin.Tier);

        return Enum.GetValues<RankTier>()
            .Where(byTier.Contains)
            .Select(tier => BuildTier(tier, byTier[tier].ToList()))
            .ToList();
    }

    private static FormBenchmarkTierReadModel BuildTier(RankTier tier, IReadOnlyCollection<FormBin> bins)
    {
        var histograms = Enum.GetValues<FormMetric>().ToDictionary(metric => metric, metric => Histogram(bins, metric));

        // Every sampled laner adds one bin of each metric, so any histogram's total is the
        // sample; the smallest is taken in case a fold was ever interrupted between them.
        var samples = histograms.Values.Min(histogram => histogram.Sum(bin => bin.Count));
        var served = samples >= MinSamples;

        PaceQuartilesReadModel? Read(FormMetric metric) => served ? Quartiles(metric, histograms[metric]) : null;

        return new FormBenchmarkTierReadModel
        {
            Tier = tier.ToRiotName(),
            Samples = samples,
            Kda = Read(FormMetric.Kda),
            KillParticipation = Read(FormMetric.KillParticipation),
            CsPerMinute = Read(FormMetric.CsPerMinute),
            Deaths = Read(FormMetric.Deaths),
            VisionPerMinute = Read(FormMetric.VisionPerMinute),
        };
    }

    private static List<(int Bucket, long Count)> Histogram(IEnumerable<FormBin> bins, FormMetric metric)
        => bins.Where(bin => bin.Metric == metric).Select(bin => (bin.Bucket, bin.Count)).ToList();

    private static PaceQuartilesReadModel? Quartiles(FormMetric metric, IReadOnlyCollection<(int Bucket, long Count)> histogram)
    {
        var median = FormHistogram.Percentile(metric, histogram, 0.5);
        if (median is null)
        {
            return null;
        }

        // Kill participation is a fraction: three decimals keep a whole percent's precision.
        var digits = metric == FormMetric.KillParticipation ? 3 : 2;
        return new PaceQuartilesReadModel
        {
            P25 = Math.Round(FormHistogram.Percentile(metric, histogram, 0.25)!.Value, digits),
            Median = Math.Round(median.Value, digits),
            P75 = Math.Round(FormHistogram.Percentile(metric, histogram, 0.75)!.Value, digits),
        };
    }

    internal sealed record FormBin(RankTier Tier, FormMetric Metric, int Bucket, long Count);
}
