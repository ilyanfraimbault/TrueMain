namespace Data.Metrics.Mongo;

/// <summary>
/// Write and read access to the Api's per-minute ops request-latency rollups (#2307). Both
/// halves on one interface, like <see cref="IMeterRollupStore"/>: the Api's flusher writes, the
/// Api's ops query reads.
/// </summary>
public interface IRequestLatencyStore
{
    /// <summary>
    /// Folds <paramref name="rollups"/> into the collection, one <c>$inc</c>-upsert per minute,
    /// route and method, so two flushes of the same minute add up.
    /// </summary>
    /// <returns>Rollups written, or 0 when Mongo is not configured.</returns>
    Task<int> WriteAsync(IReadOnlyCollection<RequestLatencyRollup> rollups, CancellationToken ct);

    /// <summary>
    /// Every route's minutes at or after <paramref name="sinceUtc"/>, summed into bins of
    /// <paramref name="binHours"/> hours (aligned on UTC midnight). Empty when Mongo is not
    /// configured.
    /// </summary>
    Task<RequestLatencyWindow> GetWindowAsync(DateTime sinceUtc, int binHours, CancellationToken ct);
}

/// <summary>One minute of one route, as folded in memory before it is written.</summary>
public sealed record RequestLatencyRollup(
    DateTime BucketStartUtc,
    string Route,
    string Method,
    long Count,
    double SumMs,
    double MaxMs,
    IReadOnlyList<long> Histogram,
    long Timeouts,
    long ServerErrors,
    DateTime LastRecordedAtUtc);

/// <summary>One route's requests summed over one bin of the window.</summary>
public sealed record RequestLatencyBin(
    string Route,
    string Method,
    DateTime BinStartUtc,
    long Count,
    double SumMs,
    double MaxMs,
    IReadOnlyList<long> Histogram,
    long Timeouts,
    long ServerErrors);

/// <summary>
/// The bins of a window, and the oldest minute the collection still retains — null when it
/// holds none, meaning nothing was ever recorded, which must not read as a quiet window.
/// </summary>
public sealed record RequestLatencyWindow(
    DateTime SinceUtc,
    IReadOnlyList<RequestLatencyBin> Bins,
    DateTime? OldestRetainedBucketUtc);
