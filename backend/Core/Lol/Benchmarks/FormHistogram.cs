namespace Core.Lol.Benchmarks;

/// <summary>
/// The fixed-width histogram the form benchmark (#2076) stores per (patch, tier, position,
/// metric): the ingestor folds a game's value into <see cref="ToBucket"/>, the API reads
/// quartiles back with <see cref="Percentile"/>. Shared so both sides agree on the bin
/// width, as <c>PaceHistogram</c> is for the pace benchmark.
/// </summary>
public static class FormHistogram
{
    /// <summary>
    /// Bin width of a metric, a little finer than the precision the tiles print a figure
    /// at (KDA to two decimals, the others to one or a whole percent): the interpolation
    /// inside a bin does the rest.
    /// </summary>
    public static double BucketWidth(FormMetric metric) => metric switch
    {
        FormMetric.Kda => 0.1,
        FormMetric.KillParticipation => 0.01,
        FormMetric.CsPerMinute => 0.1,
        FormMetric.Deaths => 1,
        FormMetric.VisionPerMinute => 0.05,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };

    /// <summary>
    /// The highest value with a bin of its own; anything above lands in the top bin. Far
    /// past any quartile (a 30 KDA, 15 CS a minute), it only keeps a freak game from
    /// opening hundreds of empty bins.
    /// </summary>
    public static double Ceiling(FormMetric metric) => metric switch
    {
        FormMetric.Kda => 30,
        FormMetric.KillParticipation => 1,
        FormMetric.CsPerMinute => 15,
        FormMetric.Deaths => 30,
        FormMetric.VisionPerMinute => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };

    /// <summary>
    /// The bin a value falls in, clamped to <c>[0, Ceiling]</c>. A hair of tolerance keeps
    /// a value sitting exactly on a bin edge (0.3 / 0.1 = 2.999…) in the bin it names.
    /// </summary>
    public static int ToBucket(FormMetric metric, double value)
    {
        var width = BucketWidth(metric);
        var clamped = Math.Clamp(double.IsFinite(value) ? value : 0, 0, Ceiling(metric));
        return (int)Math.Floor((clamped / width) + 1e-9);
    }

    /// <inheritdoc cref="BinnedHistogram.Percentile"/>
    public static double? Percentile(
        FormMetric metric,
        IReadOnlyCollection<(int Bucket, long Count)> histogram,
        double quantile)
        => BinnedHistogram.Percentile(histogram, quantile, BucketWidth(metric));
}
