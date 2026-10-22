namespace Core.Lol.Benchmarks;

/// <summary>
/// Reading percentiles back from a fixed-width histogram — the shape both benchmarks the
/// desktop app reads are stored in (the pace benchmark, #1912, and the form benchmark,
/// #2076). The bins are additive counters, so the ingestor folds a value as an upsert and
/// the API reads quartiles off the summed bins.
/// </summary>
public static class BinnedHistogram
{
    /// <summary>
    /// The <paramref name="quantile"/> (0–1) of a histogram whose bin <c>b</c> covers
    /// <c>[b × width, (b + 1) × width)</c>, interpolated linearly inside the bin it falls in —
    /// the bin's samples are assumed spread evenly over its width. Returns
    /// <see langword="null"/> for an empty histogram.
    /// </summary>
    public static double? Percentile(
        IReadOnlyCollection<(int Bucket, long Count)> histogram,
        double quantile,
        double width)
    {
        var total = histogram.Sum(bin => bin.Count);
        if (total <= 0)
        {
            return null;
        }

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
