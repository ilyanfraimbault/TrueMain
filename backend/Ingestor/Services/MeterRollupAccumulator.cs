using System.Diagnostics.Metrics;
using System.Globalization;
using Data.Metrics.Mongo;

namespace Ingestor.Services;

/// <summary>
/// Folds the measurements a <see cref="MeterListener"/> hands over into per-minute
/// <see cref="MeterRollup"/>s, one per instrument and tag set, until the exporter drains
/// them (#1636). Thread-safe: instruments are recorded from every ingestor loop at once.
/// </summary>
/// <remarks>
/// A lock, not a lock-free map: the instruments it serves fire on failures, 429s and
/// throttled Riot calls — tens a second at the most — and a drain has to swap the whole
/// set atomically so no measurement lands in a rollup that was already written.
/// </remarks>
public sealed class MeterRollupAccumulator(TimeProvider timeProvider)
{
    private readonly Lock _gate = new();
    private Dictionary<(DateTime Bucket, string Instrument, string SeriesKey), Series> _series = [];

    /// <summary>Folds one measurement of <paramref name="instrument"/> into its minute.</summary>
    public void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var bucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        var tagMap = new Dictionary<string, string>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            tagMap[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var key = (bucket, instrument.Name, MeterRollup.SeriesKeyOf(tagMap));

        lock (_gate)
        {
            if (!_series.TryGetValue(key, out var series))
            {
                series = new Series(instrument, tagMap);
                _series[key] = series;
            }

            series.Add(value, now);
        }
    }

    /// <summary>
    /// Hands over everything folded since the previous drain and starts afresh. The caller
    /// owns the result: a failed write drops it rather than putting it back, telemetry
    /// being the one thing the pipeline may lose.
    /// </summary>
    public IReadOnlyList<MeterRollup> Drain()
    {
        Dictionary<(DateTime Bucket, string Instrument, string SeriesKey), Series> drained;
        lock (_gate)
        {
            if (_series.Count == 0)
            {
                return [];
            }

            drained = _series;
            _series = [];
        }

        return drained
            .Select(entry => new MeterRollup(
                entry.Key.Bucket,
                entry.Value.Instrument.Meter.Name,
                entry.Key.Instrument,
                KindOf(entry.Value.Instrument),
                entry.Value.Instrument.Unit,
                entry.Value.Instrument.Description,
                entry.Value.Tags,
                entry.Value.Count,
                entry.Value.Sum,
                entry.Value.Max,
                entry.Value.LastRecordedAtUtc))
            .ToList();
    }

    private static string KindOf(Instrument instrument)
        => instrument.GetType().IsGenericType
           && instrument.GetType().GetGenericTypeDefinition() == typeof(Histogram<>)
            ? MeterRollup.HistogramKind
            : MeterRollup.CounterKind;

    private sealed class Series(Instrument instrument, IReadOnlyDictionary<string, string> tags)
    {
        public Instrument Instrument { get; } = instrument;
        public IReadOnlyDictionary<string, string> Tags { get; } = tags;
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Max { get; private set; } = double.MinValue;
        public DateTime LastRecordedAtUtc { get; private set; }

        public void Add(double value, DateTime recordedAtUtc)
        {
            Count++;
            Sum += value;
            Max = Math.Max(Max, value);
            LastRecordedAtUtc = recordedAtUtc;
        }
    }
}
