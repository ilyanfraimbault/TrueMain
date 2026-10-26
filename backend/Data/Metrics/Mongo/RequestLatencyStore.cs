using Data.Logging.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Mongo adapter for the ops request-latency rollups (#2307). Direct-call, no channel, like
/// <see cref="MeterRollupStore"/>: the Api's accumulator already folds the requests in memory
/// and its flusher writes a handful of documents at a time.
/// </summary>
/// <remarks>
/// Owns its collection rather than adding it to <see cref="MongoLogContext"/>, reaching it
/// through the context's database: same client, same database.
/// </remarks>
internal sealed class RequestLatencyStore(MongoLogContext context, IOptions<MongoLoggingOptions> options)
    : IRequestLatencyStore
{
    private const int DuplicateKeyCode = 11000;

    private static readonly BulkWriteOptions UnorderedBulk = new() { IsOrdered = false };
    private static readonly UpdateDefinitionBuilder<RequestLatencyRollupDocument> Update =
        Builders<RequestLatencyRollupDocument>.Update;
    private static readonly FilterDefinitionBuilder<RequestLatencyRollupDocument> Filter =
        Builders<RequestLatencyRollupDocument>.Filter;

    private readonly MongoLoggingOptions _options = options.Value;

    private int _indexesEnsured;

    private IMongoCollection<RequestLatencyRollupDocument> Rollups =>
        field ??= context.Database.GetCollection<RequestLatencyRollupDocument>(_options.RequestLatencyCollection);

    public async Task<int> WriteAsync(IReadOnlyCollection<RequestLatencyRollup> rollups, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rollups);

        if (!context.IsActive || rollups.Count == 0)
        {
            return 0;
        }

        await EnsureIndexesAsync(ct);

        var writes = rollups.Select(ToUpsert).ToList();
        try
        {
            await Rollups.BulkWriteAsync(writes, UnorderedBulk, ct);
        }
        catch (MongoBulkWriteException<RequestLatencyRollupDocument> ex)
            when (ex.WriteErrors.All(error => error.Code == DuplicateKeyCode))
        {
            // Two flushes inserted the same minute at once (a rolling deploy runs two Api
            // containers for a moment). Retried once: the upsert now finds the document.
            var retry = ex.WriteErrors.Select(error => writes[error.Index]).ToList();
            await Rollups.BulkWriteAsync(retry, UnorderedBulk, ct);
        }

        return writes.Count;
    }

    public async Task<RequestLatencyWindow> GetWindowAsync(DateTime sinceUtc, int binHours, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(binHours, 1);

        if (!context.IsActive)
        {
            return new RequestLatencyWindow(sinceUtc, [], null);
        }

        var group = new BsonDocument
        {
            {
                "_id", new BsonDocument
                {
                    { "route", "$route" },
                    { "method", "$method" },
                    {
                        "bin", new BsonDocument("$dateTrunc", new BsonDocument
                        {
                            { "date", "$bucketStartUtc" },
                            { "unit", "hour" },
                            { "binSize", binHours }
                        })
                    }
                }
            },
            { "count", new BsonDocument("$sum", "$count") },
            { "sumMs", new BsonDocument("$sum", "$sumMs") },
            { "maxMs", new BsonDocument("$max", "$maxMs") },
            { "timeouts", new BsonDocument("$sum", "$timeouts") },
            { "serverErrors", new BsonDocument("$sum", "$serverErrors") }
        };

        // Summed bucket by bucket in Mongo so only one document per route and bin travels.
        for (var index = 0; index < RequestLatencyHistogram.BucketCount; index++)
        {
            group.Add($"h{index}", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray
            {
                $"$histogram.{RequestLatencyRollupDocument.BucketKey(index)}",
                0
            })));
        }

        var pipeline = PipelineDefinition<RequestLatencyRollupDocument, BsonDocument>.Create(
        [
            new BsonDocument("$match", new BsonDocument("bucketStartUtc", new BsonDocument("$gte", sinceUtc))),
            new BsonDocument("$group", group)
        ]);

        var groups = await Rollups.Aggregate(pipeline, cancellationToken: ct).ToListAsync(ct);

        var oldest = await Rollups
            .Find(FilterDefinition<RequestLatencyRollupDocument>.Empty)
            .SortBy(doc => doc.BucketStartUtc)
            .Limit(1)
            .Project(doc => (DateTime?)doc.BucketStartUtc)
            .FirstOrDefaultAsync(ct);

        return new RequestLatencyWindow(
            sinceUtc,
            groups.Select(ToBin).ToList(),
            oldest is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null);
    }

    private UpdateOneModel<RequestLatencyRollupDocument> ToUpsert(RequestLatencyRollup rollup)
    {
        var filter = Filter.And(
            Filter.Eq(doc => doc.BucketStartUtc, rollup.BucketStartUtc),
            Filter.Eq(doc => doc.Route, rollup.Route),
            Filter.Eq(doc => doc.Method, rollup.Method));

        var updates = new List<UpdateDefinition<RequestLatencyRollupDocument>>
        {
            Update.Inc(doc => doc.Count, rollup.Count),
            Update.Inc(doc => doc.SumMs, rollup.SumMs),
            Update.Max(doc => doc.MaxMs, rollup.MaxMs),
            Update.Inc(doc => doc.Timeouts, rollup.Timeouts),
            Update.Inc(doc => doc.ServerErrors, rollup.ServerErrors),
            Update.Max(doc => doc.LastRecordedAtUtc, rollup.LastRecordedAtUtc)
        };

        // `$inc` on `histogram.bN` creates the field on insert and adds to it after; a zero is
        // skipped, so a sparse minute writes only the buckets it touched.
        for (var index = 0; index < Math.Min(rollup.Histogram.Count, RequestLatencyHistogram.BucketCount); index++)
        {
            if (rollup.Histogram[index] != 0)
            {
                updates.Add(Update.Inc(
                    $"histogram.{RequestLatencyRollupDocument.BucketKey(index)}", rollup.Histogram[index]));
            }
        }

        if (_options.ProcessName is { Length: > 0 } processName)
        {
            updates.Add(Update.Set(doc => doc.ProcessName, processName));
        }

        return new UpdateOneModel<RequestLatencyRollupDocument>(filter, Update.Combine(updates)) { IsUpsert = true };
    }

    private static RequestLatencyBin ToBin(BsonDocument group)
    {
        var id = group["_id"].AsBsonDocument;
        var histogram = new long[RequestLatencyHistogram.BucketCount];
        for (var index = 0; index < histogram.Length; index++)
        {
            histogram[index] = group[$"h{index}"].ToInt64();
        }

        return new RequestLatencyBin(
            id["route"].AsString,
            id["method"].AsString,
            id["bin"].ToUniversalTime(),
            group["count"].ToInt64(),
            group["sumMs"].ToDouble(),
            group["maxMs"].ToDouble(),
            histogram,
            group["timeouts"].ToInt64(),
            group["serverErrors"].ToInt64());
    }

    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) != 0)
        {
            return;
        }

        var models = new List<CreateIndexModel<RequestLatencyRollupDocument>>
        {
            // The upsert key; unique so two Api containers cannot split one minute in two.
            new(Builders<RequestLatencyRollupDocument>.IndexKeys
                    .Ascending(doc => doc.BucketStartUtc)
                    .Ascending(doc => doc.Route)
                    .Ascending(doc => doc.Method),
                new CreateIndexOptions { Name = "ux_bucket_route_method", Unique = true })
        };

        await Rollups.Indexes.CreateManyAsync(models, ct);
        // The window scan and the oldest-bucket lookup both lead on bucketStartUtc, which the
        // TTL index (and the unique key's prefix) already covers.
        await MongoIndexes.ReconcileTtlAsync(Rollups, doc => doc.BucketStartUtc, _options.RequestLatencyRetention, ct);

        Volatile.Write(ref _indexesEnsured, 1);
    }
}
