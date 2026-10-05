namespace Core.Lol.Pace;

/// <summary>
/// The fixed-width histogram the pace benchmark (#1912) stores per (patch, tier, position,
/// minute, metric): the ingestor folds a value into <see cref="ToBucket"/>, the API reads
/// percentiles back with <see cref="Percentile"/>. Shared so both sides agree on the bin
/// width — a width changed on one side only would shift every percentile the API serves.
/// </summary>
/// <remarks>
/// Histograms rather than sums: a mean cannot say where a player stands, and the bins stay
/// additive, so the fold is an <c>ON CONFLICT … + EXCLUDED</c> upsert like every other
/// incremental aggregate.
/// </remarks>
public static class PaceHistogram
{
    /// <summary>
    /// The last minute folded. Past half an hour the ranked games that are still running
    /// thin out quickly, and the overlay compares the laning and mid game.
    /// </summary>
    public const int MaxMinute = 30;

    /// <summary>
    /// Bin width of a metric: 5 CS (a wave is six minions) and 200 gold. At 30 minutes a
    /// lane sits around 250 CS and 12 000 gold, so either is about sixty bins, and the
    /// interpolation inside a bin keeps the percentiles finer than the width.
    /// </summary>
    public static int BucketWidth(PaceMetric metric) => metric switch
    {
        PaceMetric.Cs => 5,
        PaceMetric.GoldEarned => 200,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };

    /// <summary>The bin a cumulative value falls in; negative values (never sent by Riot) clamp to 0.</summary>
    public static int ToBucket(PaceMetric metric, int value)
        => Math.Max(0, value) / BucketWidth(metric);

    /// <summary>
    /// The <paramref name="quantile"/> (0–1) of a histogram, interpolated linearly inside the
    /// bin it falls in — the bin's samples are assumed spread evenly over its width. Returns
    /// <see langword="null"/> for an empty histogram.
    /// </summary>
    public static double? Percentile(
        PaceMetric metric,
        IReadOnlyCollection<(int Bucket, long Count)> histogram,
        double quantile)
    {
        var total = histogram.Sum(bin => bin.Count);
        if (total <= 0)
        {
            return null;
        }

        var width = BucketWidth(metric);
        var target = Math.Clamp(quantile, 0, 1) * total;
        long seen = 0;

        foreach (var (bucket, count) in histogram.Where(bin => bin.Count > 0).OrderBy(bin => bin.Bucket))
        {
            if (seen + count >= target)
            {
                var within = (target - seen) / count;
                return (bucket + within) * width;
            }

            seen += count;
        }

        // Only reachable through rounding at quantile 1: the top of the last bin.
        var last = histogram.Where(bin => bin.Count > 0).Max(bin => bin.Bucket);
        return (last + 1d) * width;
    }
}
