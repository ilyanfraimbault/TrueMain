namespace Data.Metrics.Mongo;

/// <summary>
/// The fixed duration buckets the request-latency rollups count into (#2307), and the
/// percentile estimate read back from them. One histogram per minute per route sums across
/// minutes by adding bucket counts, which an exact percentile cannot do: p95 is computed at
/// read time from the summed counts.
/// </summary>
/// <remarks>
/// The bounds are upper limits in milliseconds, inclusive; a final open bucket holds
/// everything slower than the last one. They are dense around the one-second line the admin
/// highlights and the five-second line the epic (#2300) calls slow. Changing them changes
/// what a stored array means, so a new bound is a new collection, not an edit.
/// </remarks>
public static class RequestLatencyHistogram
{
    /// <summary>Upper bounds of every bucket but the last, in milliseconds.</summary>
    public static IReadOnlyList<double> UpperBoundsMs { get; } =
        [10, 25, 50, 100, 250, 500, 750, 1000, 1500, 2500, 5000, 10000, 30000];

    /// <summary>Number of buckets, the open-ended last one included.</summary>
    public static int BucketCount => UpperBoundsMs.Count + 1;

    /// <summary>The bucket a request lasting <paramref name="durationMs"/> falls in.</summary>
    public static int BucketOf(double durationMs)
    {
        for (var index = 0; index < UpperBoundsMs.Count; index++)
        {
            if (durationMs <= UpperBoundsMs[index])
            {
                return index;
            }
        }

        return UpperBoundsMs.Count;
    }

    /// <summary>
    /// The <paramref name="quantile"/> (0..1) of the durations counted in
    /// <paramref name="buckets"/>, interpolated linearly inside the bucket it falls in and
    /// capped at <paramref name="maxMs"/>, the slowest request actually seen — so the open
    /// last bucket has an upper edge, and an estimate never exceeds a real measurement.
    /// Null when nothing was counted.
    /// </summary>
    public static double? Percentile(IReadOnlyList<long> buckets, double quantile, double maxMs)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantile, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantile, 1);

        var total = buckets.Sum();
        if (total <= 0)
        {
            return null;
        }

        var rank = quantile * total;
        long before = 0;
        for (var index = 0; index < buckets.Count; index++)
        {
            var inBucket = buckets[index];
            if (inBucket <= 0 || before + inBucket < rank)
            {
                before += Math.Max(0, inBucket);
                continue;
            }

            var lower = index == 0 ? 0 : UpperBoundsMs[index - 1];
            var upper = index < UpperBoundsMs.Count ? Math.Min(UpperBoundsMs[index], maxMs) : maxMs;
            upper = Math.Max(upper, lower);
            var share = (rank - before) / inBucket;
            return Math.Min(lower + ((upper - lower) * share), maxMs);
        }

        return maxMs;
    }

    /// <summary>Element-wise sum of <paramref name="histograms"/>, padded to <see cref="BucketCount"/>.</summary>
    public static long[] Sum(IEnumerable<IReadOnlyList<long>> histograms)
    {
        ArgumentNullException.ThrowIfNull(histograms);
        var total = new long[BucketCount];
        foreach (var histogram in histograms)
        {
            for (var index = 0; index < Math.Min(histogram.Count, total.Length); index++)
            {
                total[index] += histogram[index];
            }
        }

        return total;
    }
}
