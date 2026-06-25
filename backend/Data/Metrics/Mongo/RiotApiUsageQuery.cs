using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Purpose-built read query over the <c>riot_api_call_rollups</c> collection
/// backing the admin <c>/ops/riot-usage</c> panel (#93). Keeps Mongo concerns in
/// the Data layer: the Api stays persistence-ignorant and consumes the
/// <see cref="RiotApiUsage"/> read-model. Reuses the shared
/// <see cref="MongoLogContext"/> Mongo client (the metrics collection lives in the
/// same database as the diagnostic logs). Each document already aggregates a
/// minute of calls, so the group stages sum <c>count</c>/<c>sumLatencyMs</c>
/// instead of counting raw documents.
/// </summary>
public sealed class RiotApiUsageQuery(MongoLogContext context) : IRiotApiUsageQuery
{
    private static readonly FilterDefinitionBuilder<RiotApiCallRollupDocument> Filter =
        Builders<RiotApiCallRollupDocument>.Filter;

    // Errored-call count contributed by a rollup: 0 when the status is a success
    // (200 <= statusCode < 400), else the whole rollup's count — transport faults
    // (0), 429 and 5xx all count. Reused across every group stage so the breakdowns
    // stay consistent.
    private static readonly BsonDocument ErrorCount = new("$cond", new BsonArray
    {
        new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$gte", new BsonArray { "$statusCode", 200 }),
            new BsonDocument("$lt", new BsonArray { "$statusCode", 400 })
        }),
        0,
        "$count"
    });

    public async Task<RiotApiUsage> GetAsync(
        RiotUsageWindow window,
        string? endpoint,
        CancellationToken ct)
    {
        var (since, unit, binSize) = ResolveWindow(window);

        // An inactive store (no Mongo configured) yields an empty result rather
        // than throwing, so /ops/riot-usage degrades gracefully.
        if (!context.IsActive)
        {
            return new RiotApiUsage(since, 0, 0, 0, [], [], [], null);
        }

        var normalizedEndpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        var windowFilter = Filter.Gte(doc => doc.BucketStartUtc, since);
        var filter = windowFilter;
        if (normalizedEndpoint is not null)
        {
            filter &= Filter.Eq(doc => doc.Endpoint, normalizedEndpoint);
        }

        // The four reads are independent, so run them concurrently — the Mongo
        // driver is thread-safe and pools connections, so this cuts the panel's
        // latency to the slowest single aggregation instead of their sum.
        var endpointsTask = AggregateEndpointsAsync(filter, ct);
        var statusCodesTask = AggregateStatusCodesAsync(filter, ct);
        var timeSeriesTask = AggregateTimeSeriesAsync(filter, unit, binSize, ct);
        // The rate-limit snapshot uses the window-only filter, NOT the endpoint
        // filter: X-App-Rate-Limit[-Count] is app-wide (not per endpoint), so the
        // freshest snapshot in the window reflects true current app consumption
        // regardless of which endpoint the user is inspecting.
        var rateLimitTask = LatestRateLimitAsync(windowFilter, ct);

        await Task.WhenAll(endpointsTask, statusCodesTask, timeSeriesTask, rateLimitTask);

        var endpoints = await endpointsTask;
        var statusCodes = await statusCodesTask;
        var timeSeries = await timeSeriesTask;
        var rateLimit = await rateLimitTask;

        // Totals are derived from the per-endpoint rollup so the collection is only
        // grouped once for them (avoids a redundant whole-window aggregation). The
        // mean uses the unrounded latency sums (Σ sum / Σ calls), not a re-weighting
        // of the per-endpoint averages, so no floating-point error accumulates.
        var totalCalls = endpoints.Sum(e => e.Calls);
        var totalErrors = endpoints.Sum(e => e.Errors);
        var totalLatency = endpoints.Sum(e => e.SumLatencyMs);
        var avgLatency = totalCalls > 0 ? (double)totalLatency / totalCalls : 0;

        return new RiotApiUsage(
            since,
            totalCalls,
            totalErrors,
            avgLatency,
            endpoints,
            statusCodes,
            timeSeries,
            rateLimit);
    }

    private async Task<IReadOnlyList<RiotApiEndpointUsage>> AggregateEndpointsAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        var group = new BsonDocument
        {
            { "_id", "$endpoint" },
            { "calls", new BsonDocument("$sum", "$count") },
            { "errors", new BsonDocument("$sum", ErrorCount) },
            { "sumLatency", new BsonDocument("$sum", "$sumLatencyMs") },
            { "lastCalledAtUtc", new BsonDocument("$max", "$lastCalledAtUtc") }
        };

        var rows = await context.RiotApiCallRollups
            .Aggregate()
            .Match(filter)
            .Group<BsonDocument>(group)
            .Sort(new BsonDocument("calls", -1))
            .ToListAsync(ct);

        return rows.Select(row =>
        {
            var calls = row["calls"].ToInt64();
            var errors = row["errors"].ToInt64();
            var sumLatency = row["sumLatency"].ToInt64();
            return new RiotApiEndpointUsage(
                row["_id"].IsBsonNull ? string.Empty : row["_id"].AsString,
                calls,
                calls - errors,
                errors,
                calls > 0 ? (double)sumLatency / calls : 0,
                sumLatency,
                row["lastCalledAtUtc"].ToUniversalTime());
        }).ToList();
    }

    private async Task<IReadOnlyList<RiotApiStatusCount>> AggregateStatusCodesAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        var group = new BsonDocument
        {
            { "_id", "$statusCode" },
            { "count", new BsonDocument("$sum", "$count") }
        };

        var rows = await context.RiotApiCallRollups
            .Aggregate()
            .Match(filter)
            .Group<BsonDocument>(group)
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync(ct);

        return rows
            .Select(row => new RiotApiStatusCount(row["_id"].ToInt32(), row["count"].ToInt64()))
            .ToList();
    }

    private async Task<IReadOnlyList<RiotApiUsageBucket>> AggregateTimeSeriesAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        string unit,
        int binSize,
        CancellationToken ct)
    {
        // $dateTrunc (Mongo 5.0+) snaps each minute-rollup down to the display
        // bucket boundary so calls group into fixed windows for the volume chart.
        // The display bins (5 min / 1 h / 6 h) are all multiples of the 1-minute
        // rollup granularity, so re-bucketing stays exact.
        var bucket = new BsonDocument("$dateTrunc", new BsonDocument
        {
            { "date", "$bucketStartUtc" },
            { "unit", unit },
            { "binSize", binSize }
        });

        var group = new BsonDocument
        {
            { "_id", bucket },
            { "calls", new BsonDocument("$sum", "$count") },
            { "errors", new BsonDocument("$sum", ErrorCount) }
        };

        var rows = await context.RiotApiCallRollups
            .Aggregate()
            .Match(filter)
            .Group<BsonDocument>(group)
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync(ct);

        return rows
            .Select(row => new RiotApiUsageBucket(
                row["_id"].ToUniversalTime(),
                row["calls"].ToInt64(),
                row["errors"].ToInt64()))
            .ToList();
    }

    private async Task<RiotApiRateLimitSnapshot?> LatestRateLimitAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        // The app rate-limit count is on (almost) every Riot response, so the newest
        // rollup carries the most recent "current" consumption seen in the window.
        // $ne null (not $exists) so a rollup whose field is present-but-null can never
        // mask an older rollup with a real value.
        var withHeaders = filter & Filter.Ne(doc => doc.AppRateLimitCount, (string?)null);

        // Sort on BucketStartUtc (not LastCalledAtUtc) so the ix_timestamp_desc index
        // serves both the range filter and the sort — no in-memory sort. The two are
        // always in the same minute, far below the 5-minute display bin, so the
        // sub-minute skew is immaterial; ObservedAtUtc below still reports the precise
        // last-call time.
        var doc = await context.RiotApiCallRollups
            .Find(withHeaders)
            .Sort(Builders<RiotApiCallRollupDocument>.Sort.Descending(d => d.BucketStartUtc))
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (doc is null)
        {
            return null;
        }

        return new RiotApiRateLimitSnapshot(
            doc.LastCalledAtUtc,
            doc.AppRateLimit,
            doc.AppRateLimitCount,
            doc.MethodRateLimit,
            doc.MethodRateLimitCount,
            doc.RetryAfterSeconds,
            doc.RateLimitType);
    }

    private static (DateTime Since, string Unit, int BinSize) ResolveWindow(RiotUsageWindow window)
    {
        var now = DateTime.UtcNow;
        return window switch
        {
            RiotUsageWindow.LastHour => (now.AddHours(-1), "minute", 5),
            RiotUsageWindow.Last7Days => (now.AddDays(-7), "hour", 6),
            _ => (now.AddHours(-24), "hour", 1)
        };
    }
}
