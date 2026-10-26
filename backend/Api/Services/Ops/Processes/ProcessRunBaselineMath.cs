using System.Text.Json;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Processes;

/// <summary>
/// The thresholds a run is judged against on the Processes page (#2298) — the one place
/// to tune them. A metric is flagged when it is at least <see cref="HighDeviation"/>
/// above its median (twice the usual) or at least <see cref="LowDeviation"/> below it
/// (half the usual); nothing is compared before <see cref="MinSamples"/> successful runs.
/// </summary>
internal static class ProcessRunBaselinePolicy
{
    /// <summary>How many of the latest successful runs make the median.</summary>
    public const int SampleSize = 20;

    /// <summary>Below this many runs (or runs recording a counter) there is no baseline.</summary>
    public const int MinSamples = 5;

    public const double HighDeviation = 1.0;

    public const double LowDeviation = -0.5;

    /// <summary>Baselines move with each run, so they are only reused for a short while.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
}

/// <summary>
/// The medians of one (process, mode) history: duration and every top-level numeric
/// summary counter, each with the number of runs it was taken over.
/// </summary>
internal sealed record ProcessRunBaselineStats(
    int SampleCount,
    double? DurationMedianMs,
    IReadOnlyDictionary<string, double> CounterMedians);

/// <summary>Pure maths behind the run baselines: medians, deviations, thresholds.</summary>
internal static class ProcessRunBaselineMath
{
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// <c>(value − median) / median</c>; 0 when both are zero, null when only the median
    /// is (a relative gap from nothing has no meaning).
    /// </summary>
    public static double? Deviation(double value, double median)
    {
        if (median == 0)
        {
            return value == 0 ? 0 : null;
        }

        return (value - median) / median;
    }

    public static bool IsFlagged(double? deviation)
        => deviation is { } gap
           && (gap >= ProcessRunBaselinePolicy.HighDeviation || gap <= ProcessRunBaselinePolicy.LowDeviation);

    public static ProcessRunMetricBaselineReadModel Compare(double value, double median)
    {
        var deviation = Deviation(value, median);
        return new ProcessRunMetricBaselineReadModel
        {
            Median = median,
            Deviation = deviation,
            Flagged = IsFlagged(deviation),
        };
    }

    public static ProcessRunBaselineStats Summarise(IReadOnlyList<ProcessRunBaselineSample> samples)
    {
        var counterValues = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            foreach (var (name, value) in NumericCounters(sample.SummaryJson))
            {
                if (!counterValues.TryGetValue(name, out var list))
                {
                    counterValues[name] = list = [];
                }

                list.Add(value);
            }
        }

        var counterMedians = counterValues
            .Where(pair => pair.Value.Count >= ProcessRunBaselinePolicy.MinSamples)
            .ToDictionary(pair => pair.Key, pair => Median(pair.Value)!.Value, StringComparer.Ordinal);

        return new ProcessRunBaselineStats(
            samples.Count,
            samples.Count >= ProcessRunBaselinePolicy.MinSamples
                ? Median(samples.Select(sample => (double)sample.DurationMs).ToList())
                : null,
            counterMedians);
    }

    /// <summary>
    /// Measures one run against its history, or null when the history is too short.
    /// Only the summary's top-level numbers are counters: nested breakdowns (per
    /// platform, per step) are not comparable field by field.
    /// </summary>
    public static ProcessRunBaselineReadModel? Measure(
        int durationMs, string? summaryJson, ProcessRunBaselineStats stats)
    {
        if (stats.SampleCount < ProcessRunBaselinePolicy.MinSamples || stats.DurationMedianMs is not { } durationMedian)
        {
            return null;
        }

        var counters = new Dictionary<string, ProcessRunMetricBaselineReadModel>(StringComparer.Ordinal);
        foreach (var (name, value) in NumericCounters(summaryJson))
        {
            if (stats.CounterMedians.TryGetValue(name, out var median))
            {
                counters[name] = Compare(value, median);
            }
        }

        return new ProcessRunBaselineReadModel
        {
            SampleCount = stats.SampleCount,
            Duration = Compare(durationMs, durationMedian),
            Counters = counters,
        };
    }

    internal static List<(string Name, double Value)> NumericCounters(string? summaryJson)
    {
        using var document = ProcessRunSummaryParsing.TryParseObject(summaryJson);
        if (document is null)
        {
            return [];
        }

        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Number)
            .Select(property => (property.Name, property.Value.GetDouble()))
            .ToList();
    }
}
