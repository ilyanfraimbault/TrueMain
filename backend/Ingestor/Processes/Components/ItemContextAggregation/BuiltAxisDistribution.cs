using Data.ItemContext;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Where the decisions of a run sat on one in-game axis, before banding: how many were
/// measured, and the value at the 10th, 25th, 50th, 75th and 90th percentiles.
/// </summary>
public sealed record BuiltAxisQuantiles(long Decisions, double P10, double P25, double P50, double P75, double P90);

/// <summary>
/// The distribution of every in-game axis's raw value over the decisions one run folded
/// (#1750) — what <see cref="BuiltAxisThresholds"/> is to be calibrated from, since no such
/// distribution existed before the fold measured it.
/// </summary>
/// <remarks>
/// Kept as a histogram of values rounded to two decimals rather than a list of every value: an
/// armour sum is an integer under a few thousand and a share a fraction of a handful of items,
/// so the histogram's size is bounded by the axis's range, not by the number of games a full
/// re-fold walks.
/// </remarks>
public sealed class BuiltAxisDistribution
{
    private readonly Dictionary<ItemContextAxis, SortedDictionary<double, long>> _histograms = [];

    public void Add(IReadOnlyList<(ItemContextAxis Axis, double Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var (axis, value) in values)
        {
            if (!_histograms.TryGetValue(axis, out var histogram))
            {
                histogram = [];
                _histograms[axis] = histogram;
            }

            var key = Math.Round(value, 2);
            histogram[key] = histogram.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>The quantiles of every axis measured at least once, by axis name.</summary>
    public IReadOnlyDictionary<string, BuiltAxisQuantiles> Quantiles()
        => _histograms.ToDictionary(
            entry => entry.Key.ToString(),
            entry => Of(entry.Value),
            StringComparer.Ordinal);

    private static BuiltAxisQuantiles Of(SortedDictionary<double, long> histogram)
    {
        var total = histogram.Values.Sum();
        return new BuiltAxisQuantiles(
            total,
            At(histogram, total, 0.10),
            At(histogram, total, 0.25),
            At(histogram, total, 0.50),
            At(histogram, total, 0.75),
            At(histogram, total, 0.90));
    }

    /// <summary>The smallest value at or below which at least <paramref name="q"/> of the decisions sit.</summary>
    private static double At(SortedDictionary<double, long> histogram, long total, double q)
    {
        var target = Math.Max(1, (long)Math.Ceiling(q * total));
        var seen = 0L;
        foreach (var (value, count) in histogram)
        {
            seen += count;
            if (seen >= target)
            {
                return value;
            }
        }

        return histogram.Keys.Last();
    }
}
