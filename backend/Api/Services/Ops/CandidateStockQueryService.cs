using Data.Entities;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Shapes the hourly candidate-stock snapshots (#1403) into the admin panel's level
/// series. A thin adapter on the same lines as
/// <see cref="DbStorageHistoryQueryService"/>: the store owns the query, this owns the
/// arithmetic — which for a stock is picking the right reading, not adding readings up.
/// </summary>
public sealed class CandidateStockQueryService(
    ICandidateStockSnapshotStore store,
    IOptions<MongoLoggingOptions> mongoOptions,
    TimeProvider timeProvider) : ICandidateStockQueryService
{
    private const int DefaultWindowDays = 7;

    /// <summary>
    /// Upper bound on the requested window. Beyond the snapshot TTL there is nothing to
    /// find anyway, and an unbounded value would only widen the scan.
    /// </summary>
    private const int MaxWindowDays = 365;

    public async Task<CandidateStockReadModel> GetAsync(
        IngestionTimeGranularity granularity,
        int? windowDays,
        CancellationToken ct)
    {
        var days = Math.Clamp(windowDays is > 0 ? windowDays.Value : DefaultWindowDays, 1, MaxWindowDays);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var since = nowUtc.Date.AddDays(-days);

        var model = new CandidateStockReadModel
        {
            WindowDays = days,
            RetentionDays = (int)Math.Round(mongoOptions.Value.CandidateStockSnapshotsRetention.TotalDays),
        };

        var points = await store.GetHistoryAsync(since, ct);
        if (points.Count == 0)
        {
            // No snapshots yet — the step has not run, or Mongo is unconfigured. An
            // empty model is the right answer; the panel says "no history yet".
            return model;
        }

        // Two nested reductions, in this order and not the other:
        //   1. per hour, sum the platforms — disjoint populations at one instant;
        //   2. per bucket, keep the LAST hour — a level is sampled, never accumulated.
        // Doing it the other way (sum the buckets, then pick a platform) would report a
        // day of hourly readings as a level twenty-four times too high.
        var hourly = new SortedDictionary<DateTime, Dictionary<MainCandidateStatus, long>>();
        foreach (var point in points)
        {
            ct.ThrowIfCancellationRequested();

            // An unparseable status is a document from a future enum this build does not
            // know: skip it rather than folding it into a bucket it does not belong to.
            if (!Enum.TryParse<MainCandidateStatus>(point.Status, ignoreCase: false, out var status))
            {
                continue;
            }

            if (!hourly.TryGetValue(point.SnapshotHourUtc, out var byStatus))
            {
                byStatus = [];
                hourly[point.SnapshotHourUtc] = byStatus;
            }

            byStatus[status] = byStatus.GetValueOrDefault(status) + point.Count;
        }

        if (hourly.Count == 0)
        {
            return model;
        }

        // The last hour of each bucket wins. Iterating the sorted hours forward means a
        // later hour simply overwrites the bucket's entry.
        var buckets = new SortedDictionary<DateTime, (DateTime SampledAt, Dictionary<MainCandidateStatus, long> Counts)>();
        foreach (var (hour, counts) in hourly)
        {
            buckets[RunTimeBuckets.Truncate(hour, granularity)] = (hour, counts);
        }

        // No zero-fill between buckets, deliberately: a gap means the ingestor was not
        // running, and drawing a zero there would claim the funnel emptied out. The
        // chart breaks the line instead.
        return model with
        {
            Buckets = [.. buckets.Select(entry => new CandidateStockBucket(
                RunTimeBuckets.Format(entry.Key),
                entry.Value.Counts.GetValueOrDefault(MainCandidateStatus.New),
                entry.Value.Counts.GetValueOrDefault(MainCandidateStatus.Scored),
                entry.Value.Counts.GetValueOrDefault(MainCandidateStatus.Queued),
                entry.Value.Counts.GetValueOrDefault(MainCandidateStatus.Processing),
                entry.Value.Counts.GetValueOrDefault(MainCandidateStatus.Validated),
                entry.Value.Counts.GetValueOrDefault(MainCandidateStatus.Rejected),
                entry.Value.SampledAt))],
            EarliestSnapshotAtUtc = hourly.Keys.First(),
            LatestSnapshotAtUtc = hourly.Keys.Last(),
        };
    }
}
