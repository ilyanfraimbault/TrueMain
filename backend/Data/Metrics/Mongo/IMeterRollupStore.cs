namespace Data.Metrics.Mongo;

/// <summary>
/// Write and read access to the per-minute meter rollups (#1636). Both halves on one
/// interface, like <see cref="ICandidateStockSnapshotStore"/>: the Ingestor's exporter
/// writes, the Api reads, and they are two ends of one small collection.
/// </summary>
public interface IMeterRollupStore
{
    /// <summary>
    /// Folds <paramref name="rollups"/> into the collection, one <c>$inc</c>-upsert per
    /// minute, instrument and tag set, so two flushes of the same minute add up.
    /// </summary>
    /// <returns>Rollups written, or 0 when Mongo is not configured.</returns>
    Task<int> WriteAsync(IReadOnlyCollection<MeterRollup> rollups, CancellationToken ct);

    /// <summary>
    /// Every series of <paramref name="meter"/> measured at or after
    /// <paramref name="sinceUtc"/>, summed over the window. Empty when Mongo is not
    /// configured, so the panel degrades to "nothing recorded" instead of failing.
    /// </summary>
    Task<MeterRollupWindow> GetWindowAsync(string meter, DateTime sinceUtc, CancellationToken ct);
}

/// <summary>One minute of one instrument for one tag set, as folded in memory before it is written.</summary>
public sealed record MeterRollup(
    DateTime BucketStartUtc,
    string Meter,
    string Instrument,
    string Kind,
    string? Unit,
    string? Description,
    IReadOnlyDictionary<string, string> Tags,
    long Count,
    double Sum,
    double Max,
    DateTime LastRecordedAtUtc)
{
    /// <summary><see cref="MeterRollupDocument.Kind"/> of a counter.</summary>
    public const string CounterKind = "counter";

    /// <summary><see cref="MeterRollupDocument.Kind"/> of a histogram.</summary>
    public const string HistogramKind = "histogram";

    /// <summary>
    /// The canonical form of a tag set the unique index keys on: <c>key=value</c> pairs sorted
    /// by key (ordinal), joined by <c>|</c>. An empty tag set is the empty string.
    /// </summary>
    public static string SeriesKeyOf(IReadOnlyDictionary<string, string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return string.Join('|', tags.OrderBy(tag => tag.Key, StringComparer.Ordinal)
            .Select(tag => $"{tag.Key}={tag.Value}"));
    }
}

/// <summary>
/// One series summed over a window: <paramref name="Count"/> measurements adding up to
/// <paramref name="Sum"/>, the largest being <paramref name="Max"/>.
/// </summary>
public sealed record MeterSeriesTotal(
    string Instrument,
    string Kind,
    string? Unit,
    string? Description,
    IReadOnlyDictionary<string, string> Tags,
    long Count,
    double Sum,
    double Max,
    DateTime LastRecordedAtUtc);

/// <summary>
/// The series of one meter over a window, and the oldest minute the collection still
/// retains for that meter — null when it holds none, meaning nothing was ever recorded
/// (or all of it has expired), which the reader must not present as a quiet window.
/// </summary>
public sealed record MeterRollupWindow(
    DateTime SinceUtc,
    IReadOnlyList<MeterSeriesTotal> Series,
    DateTime? OldestRetainedBucketUtc);
