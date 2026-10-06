using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Purpose-built read over <c>riot_api_call_rollups</c> for the per-host quota view
/// (#1458). Counts route-keyed rollups only: a rollup written before the route joined
/// the key carries the last-seen route of its minute, which credits several hosts' calls
/// to one — measured on prod, it put a regional host above 100% of its limit. Those
/// legacy rollups still feed the endpoint/caller panel; they expire with the retention.
/// </summary>
public sealed class RiotQuotaQuery(MongoLogContext context) : IRiotQuotaQuery
{
    private const string UnknownKey = "unknown";

    private static readonly FilterDefinitionBuilder<RiotApiCallRollupDocument> Filter =
        Builders<RiotApiCallRollupDocument>.Filter;

    // A rollup's 429 count: the whole rollup when Riot rejected it for rate, else 0.
    private static readonly BsonDocument RateLimitedCount = new("$cond", new BsonArray
    {
        new BsonDocument("$eq", new BsonArray { "$statusCode", 429 }),
        "$count",
        0
    });

    // A rollup's error count, same definition as the usage panel: anything outside
    // 2xx/3xx, transport faults (status 0) included.
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

    public async Task<RiotQuotaUsage> GetAsync(RiotUsageWindow window, CancellationToken ct)
    {
        var (since, _, _) = RiotApiUsageQuery.ResolveWindow(window);
        if (!context.IsActive)
        {
            return new RiotQuotaUsage(since, null, null, []);
        }

        var routeKeyed = Filter.Eq(doc => doc.RouteKeyed, true);
        var filter = Filter.Gte(doc => doc.BucketStartUtc, since) & routeKeyed;

        var totalsTask = AggregateRouteTotalsAsync(filter, ct);
        var consumersTask = AggregateConsumersAsync(filter, ct);
        var headersTask = AggregateLatestHeadersAsync(filter, ct);
        var oldestTask = OldestBucketAsync(FilterDefinition<RiotApiCallRollupDocument>.Empty, ct);
        var oldestRouteKeyedTask = OldestBucketAsync(routeKeyed, ct);

        await Task.WhenAll(totalsTask, consumersTask, headersTask, oldestTask, oldestRouteKeyedTask);

        var consumers = await consumersTask;
        var headers = await headersTask;

        var routes = (await totalsTask)
            .Select(total =>
            {
                headers.TryGetValue(total.Route, out var latest);
                return new RiotRouteUsage(
                    total.Route,
                    total.Calls,
                    total.RateLimited,
                    total.Errors,
                    total.ActiveMinutes,
                    latest?.AppRateLimit,
                    latest?.AppRateLimitCount,
                    latest?.ObservedAtUtc,
                    consumers.TryGetValue(total.Route, out var rows) ? rows : []);
            })
            .ToList();

        return new RiotQuotaUsage(since, await oldestTask, await oldestRouteKeyedTask, routes);
    }

    // Walks the descending bucket index backwards, so the route-keyed lookup only scans
    // the legacy rollups older than the cutover — bounded by the retention.
    private async Task<DateTime?> OldestBucketAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        var oldest = await context.RiotApiCallRollups
            .Find(filter)
            .Sort(Builders<RiotApiCallRollupDocument>.Sort.Ascending(doc => doc.BucketStartUtc))
            .Project(doc => doc.BucketStartUtc)
            .Limit(1)
            .ToListAsync(ct);
        return oldest.Count > 0 ? oldest[0] : null;
    }

    /// <summary>
    /// Per-host totals. Two group stages: first per <c>(route, minute)</c> so each active
    /// minute is counted once however many endpoints and callers shared it, then per route.
    /// </summary>
    private async Task<IReadOnlyList<RouteTotal>> AggregateRouteTotalsAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        var perMinute = new BsonDocument
        {
            { "_id", new BsonDocument { { "route", "$route" }, { "minute", "$bucketStartUtc" } } },
            { "calls", new BsonDocument("$sum", "$count") },
            { "rateLimited", new BsonDocument("$sum", RateLimitedCount) },
            { "errors", new BsonDocument("$sum", ErrorCount) }
        };
        var perRoute = new BsonDocument
        {
            { "_id", "$_id.route" },
            { "calls", new BsonDocument("$sum", "$calls") },
            { "rateLimited", new BsonDocument("$sum", "$rateLimited") },
            { "errors", new BsonDocument("$sum", "$errors") },
            { "activeMinutes", new BsonDocument("$sum", 1) }
        };

        var rows = await context.RiotApiCallRollups
            .Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Match(filter)
            .Group<BsonDocument>(perMinute)
            .Group<BsonDocument>(perRoute)
            .Sort(new BsonDocument("calls", -1))
            .ToListAsync(ct);

        return rows
            .Select(row => new RouteTotal(
                KeyOf(row["_id"]),
                row["calls"].ToInt64(),
                row["rateLimited"].ToInt64(),
                row["errors"].ToInt64(),
                row["activeMinutes"].ToInt64()))
            .ToList();
    }

    /// <summary>Calls per <c>(route, caller, endpoint)</c>, grouped by route, largest first.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<RiotRouteConsumer>>> AggregateConsumersAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        var group = new BsonDocument
        {
            {
                "_id", new BsonDocument
                {
                    { "route", "$route" },
                    { "caller", "$callerProcess" },
                    { "endpoint", "$endpoint" }
                }
            },
            { "calls", new BsonDocument("$sum", "$count") },
            { "rateLimited", new BsonDocument("$sum", RateLimitedCount) }
        };

        var rows = await context.RiotApiCallRollups
            .Aggregate()
            .Match(filter)
            .Group<BsonDocument>(group)
            .Sort(new BsonDocument("calls", -1))
            .ToListAsync(ct);

        return rows
            .GroupBy(row => KeyOf(Field(row, "route")))
            .ToDictionary(
                byRoute => byRoute.Key,
                IReadOnlyList<RiotRouteConsumer> (byRoute) => byRoute
                    .Select(row => new RiotRouteConsumer(
                        KeyOf(Field(row, "caller")),
                        KeyOf(Field(row, "endpoint")),
                        row["calls"].ToInt64(),
                        row["rateLimited"].ToInt64()))
                    .ToList());
    }

    /// <summary>
    /// The freshest app rate-limit headers each host returned in the window. Filtered to
    /// rollups that carry them and sorted newest first before the group, so <c>$first</c>
    /// per route is the most recent real reading — the same shape as the per-endpoint
    /// method-limit lookup, served by the descending bucket index.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, LatestHeaders>> AggregateLatestHeadersAsync(
        FilterDefinition<RiotApiCallRollupDocument> filter,
        CancellationToken ct)
    {
        var withHeaders = filter & Filter.Ne(doc => doc.AppRateLimitCount, (string?)null);
        var group = new BsonDocument
        {
            { "_id", "$route" },
            { "appRateLimit", new BsonDocument("$first", "$appRateLimit") },
            { "appRateLimitCount", new BsonDocument("$first", "$appRateLimitCount") },
            { "observedAtUtc", new BsonDocument("$first", "$lastCalledAtUtc") }
        };

        var rows = await context.RiotApiCallRollups
            .Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Match(withHeaders)
            .Sort(new BsonDocument("bucketStartUtc", -1))
            .Group<BsonDocument>(group)
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => KeyOf(row["_id"]),
            row => new LatestHeaders(
                row["appRateLimit"].IsBsonNull ? null : row["appRateLimit"].AsString,
                row["appRateLimitCount"].IsBsonNull ? null : row["appRateLimitCount"].AsString,
                row["observedAtUtc"].ToUniversalTime()));
    }

    // A compound group key omits a field the rollup did not have (a pre-#1035 rollup has
    // no callerProcess), so read it with a null default rather than an indexer.
    private static BsonValue Field(BsonDocument row, string name)
        => row["_id"].AsBsonDocument.GetValue(name, BsonNull.Value);

    // A missing field and an explicit null both group under BsonNull.
    private static string KeyOf(BsonValue value)
        => value.IsBsonNull || value.IsBsonUndefined ? UnknownKey : value.AsString;

    private sealed record RouteTotal(string Route, long Calls, long RateLimited, long Errors, long ActiveMinutes);

    private sealed record LatestHeaders(string? AppRateLimit, string? AppRateLimitCount, DateTime ObservedAtUtc);
}
