using Data.Metrics.Mongo;

namespace TrueMain.RequestLatency;

/// <summary>
/// Folds the ops requests the Api answers into per-minute rollups in memory (#2307), until
/// <see cref="RequestLatencyFlusher"/> drains them to Mongo. Singleton.
/// </summary>
/// <remarks>
/// <para>
/// Recording is a dictionary update under a lock that no I/O ever holds, so it never blocks a
/// request on Mongo. The ops routes are a few dozen templates and the flusher drains every
/// half-minute, so the dictionary holds a few dozen keys at a time.
/// </para>
/// <para>
/// It is bounded all the same: past <see cref="MaxPendingKeys"/> pending (minute, route,
/// method) keys — a flusher stuck behind a dead Mongo, say — a request for a new key is
/// dropped rather than grown into memory, and <see cref="DroppedRequests"/> counts it, as it
/// counts the requests of a flush that failed. The counter is this process's since it started
/// and is shown with the panel, so a gap in the data never reads as a quiet route.
/// </para>
/// </remarks>
public sealed class RequestLatencyAccumulator
{
    /// <summary>Most (minute, route, method) keys held between two flushes.</summary>
    internal const int MaxPendingKeys = 2_000;

    private readonly Lock _gate = new();
    private Dictionary<(DateTime Minute, string Route, string Method), Accumulator> _pending = [];
    private long _dropped;

    /// <summary>Requests recorded but never persisted, since this process started.</summary>
    public long DroppedRequests => Interlocked.Read(ref _dropped);

    /// <summary>Folds one request into its minute.</summary>
    public void Record(RequestLatencySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var started = sample.StartedAtUtc;
        var minute = new DateTime(
            started.Year, started.Month, started.Day, started.Hour, started.Minute, 0, DateTimeKind.Utc);
        var key = (minute, sample.Route, sample.Method);

        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var accumulator))
            {
                if (_pending.Count >= MaxPendingKeys)
                {
                    _dropped++;
                    return;
                }

                accumulator = new Accumulator();
                _pending[key] = accumulator;
            }

            accumulator.Add(sample);
        }
    }

    /// <summary>Takes every pending rollup and starts a fresh minute set.</summary>
    public IReadOnlyCollection<RequestLatencyRollup> Drain()
    {
        Dictionary<(DateTime Minute, string Route, string Method), Accumulator> drained;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            drained = _pending;
            _pending = [];
        }

        return drained
            .Select(entry => new RequestLatencyRollup(
                entry.Key.Minute,
                entry.Key.Route,
                entry.Key.Method,
                entry.Value.Count,
                entry.Value.SumMs,
                entry.Value.MaxMs,
                entry.Value.Histogram,
                entry.Value.Timeouts,
                entry.Value.ServerErrors,
                entry.Value.LastRecordedAtUtc))
            .ToList();
    }

    /// <summary>Counts the requests of rollups that could not be persisted.</summary>
    public void CountDropped(IEnumerable<RequestLatencyRollup> rollups)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        Interlocked.Add(ref _dropped, rollups.Sum(rollup => rollup.Count));
    }

    private sealed class Accumulator
    {
        public long Count { get; private set; }
        public double SumMs { get; private set; }
        public double MaxMs { get; private set; }
        public long[] Histogram { get; } = new long[RequestLatencyHistogram.BucketCount];
        public long Timeouts { get; private set; }
        public long ServerErrors { get; private set; }
        public DateTime LastRecordedAtUtc { get; private set; }

        public void Add(RequestLatencySample sample)
        {
            Count++;
            SumMs += sample.DurationMs;
            MaxMs = Math.Max(MaxMs, sample.DurationMs);
            Histogram[RequestLatencyHistogram.BucketOf(sample.DurationMs)]++;
            Timeouts += sample.TimedOut ? 1 : 0;
            ServerErrors += sample.ServerError ? 1 : 0;
            if (sample.StartedAtUtc > LastRecordedAtUtc)
            {
                LastRecordedAtUtc = sample.StartedAtUtc;
            }
        }
    }
}

/// <summary>One answered (or abandoned) ops request.</summary>
/// <param name="Route">The route template with a leading slash, e.g. <c>/ops/logs</c>.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="StartedAtUtc">When the request reached the Api.</param>
/// <param name="DurationMs">How long the Api took to answer it.</param>
/// <param name="TimedOut">The client gave up, or the request ended cancelled or with a 504.</param>
/// <param name="ServerError">It was answered with a 5xx (a timeout is not counted twice).</param>
public sealed record RequestLatencySample(
    string Route,
    string Method,
    DateTime StartedAtUtc,
    double DurationMs,
    bool TimedOut,
    bool ServerError);
