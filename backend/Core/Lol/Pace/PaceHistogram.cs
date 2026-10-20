using Core.Lol.Benchmarks;

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

    /// <inheritdoc cref="BinnedHistogram.Percentile"/>
    public static double? Percentile(
        PaceMetric metric,
        IReadOnlyCollection<(int Bucket, long Count)> histogram,
        double quantile)
        => BinnedHistogram.Percentile(histogram, quantile, BucketWidth(metric));
}
