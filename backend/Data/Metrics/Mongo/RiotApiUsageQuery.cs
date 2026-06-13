using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Purpose-built read query over the <c>riot_api_calls</c> collection backing the
/// admin <c>/ops/riot-usage</c> panel (#93). Keeps Mongo concerns in the Data
/// layer: the Api stays persistence-ignorant and consumes the
/// <see cref="RiotApiUsage"/> read-model. Reuses the shared
/// <see cref="MongoLogContext"/> Mongo client (the metrics collection lives in the
/// same database as the diagnostic logs).
/// </summary>
public sealed class RiotApiUsageQuery(MongoLogContext context) : IRiotApiUsageQuery
{
    private static readonly FilterDefinitionBuilder<RiotApiCallDocument> Filter =
        Builders<RiotApiCallDocument>.Filter;

    // error = NOT (200 <= statusCode < 400): transport faults (0), 429 and 5xx all
    // count. Reused across every group stage so the breakdowns stay consistent.
    private static readonly BsonDocument ErrorCond = new("$cond", new BsonArray
    {
        new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$gte", new BsonArray { "$statusCode", 200 }),
            new BsonDocument("$lt", new BsonArray { "$statusCode", 400 })
        }),
        0,
        1
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
        var filter = Filter.Gte(doc => doc.TimestampUtc, since);
        if (normalizedEndpoint is not null)
        {
            filter &= Filter.Eq(doc => doc.Endpoint, normalizedEndpoint);
        }

        var endpoints = await AggregateEndpointsAsync(filter, ct);
        var statusCodes = await AggregateStatusCodesAsync(filter, ct);
        var timeSeries = await AggregateTimeSeriesAsync(filter, unit, binSize, ct);
        var rateLimit = await LatestRateLimitAsync(filter, ct);

        // Totals are derived from the per-endpoint rollup so the collection is only
        // grouped once for them (avoids a redundant whole-window aggregation).
        var totalCalls = endpoints.Sum(e => e.Calls);
        var totalErrors = endpoints.Sum(e => e.Errors);
        var weightedLatency = endpoints.Sum(e => e.AvgLatencyMs * e.Calls);
        var avgLatency = totalCalls > 0 ? weightedLatency / totalCalls : 0;

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
        FilterDefinition<RiotApiCallDocument> filter,
        CancellationToken ct)
    {
        var group = new BsonDocument
        {
            { "_id", "$endpoint" },
            { "calls", new BsonDocument("$sum", 1) },
            { "errors", new BsonDocument("$sum", ErrorCond) },
            { "sumLatency", new BsonDocument("$sum", "$latencyMs") },
            { "lastCalledAtUtc", new BsonDocument("$max", "$timestampUtc") }
        };

        var rows = await context.RiotApiCalls
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
                row["lastCalledAtUtc"].ToUniversalTime());
        }).ToList();
    }

    private async Task<IReadOnlyList<RiotApiStatusCount>> AggregateStatusCodesAsync(
        FilterDefinition<RiotApiCallDocument> filter,
        CancellationToken ct)
    {
        var group = new BsonDocument
        {
            { "_id", "$statusCode" },
            { "count", new BsonDocument("$sum", 1) }
        };

        var rows = await context.RiotApiCalls
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
        FilterDefinition<RiotApiCallDocument> filter,
        string unit,
        int binSize,
        CancellationToken ct)
    {
        // $dateTrunc (Mongo 5.0+) snaps each timestamp down to the bucket boundary
        // so calls group into fixed windows for the volume chart.
        var bucket = new BsonDocument("$dateTrunc", new BsonDocument
        {
            { "date", "$timestampUtc" },
            { "unit", unit },
            { "binSize", binSize }
        });

        var group = new BsonDocument
        {
            { "_id", bucket },
            { "calls", new BsonDocument("$sum", 1) },
            { "errors", new BsonDocument("$sum", ErrorCond) }
        };

        var rows = await context.RiotApiCalls
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
        FilterDefinition<RiotApiCallDocument> filter,
        CancellationToken ct)
    {
        // The app rate-limit count is on (almost) every Riot response, so the most
        // recent call in the window carries the freshest "current" consumption.
        var withHeaders = filter & Filter.Exists(doc => doc.AppRateLimitCount);

        var doc = await context.RiotApiCalls
            .Find(withHeaders)
            .Sort(Builders<RiotApiCallDocument>.Sort
                .Descending(d => d.TimestampUtc)
                .Descending(d => d.Id))
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (doc is null)
        {
            return null;
        }

        return new RiotApiRateLimitSnapshot(
            doc.TimestampUtc,
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
