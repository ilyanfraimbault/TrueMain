using Data.Logging.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Mongo adapter for the meter rollups (#1636). Direct-call, no channel: the exporter that
/// writes already folds a minute of measurements in memory and flushes once a minute, so
/// there is nothing left to smooth.
/// </summary>
/// <remarks>
/// Owns its collection rather than adding it to <see cref="MongoLogContext"/>, reaching it
/// through the context's database like <c>DesktopTelemetryStore</c>: same client, same
/// database.
/// </remarks>
internal sealed class MeterRollupStore(MongoLogContext context, IOptions<MongoLoggingOptions> options)
    : IMeterRollupStore
{
    private const int DuplicateKeyCode = 11000;

    // Unordered: one failed upsert must not abort the rest of the flush.
    private static readonly BulkWriteOptions UnorderedBulk = new() { IsOrdered = false };
    private static readonly UpdateDefinitionBuilder<MeterRollupDocument> Update = Builders<MeterRollupDocument>.Update;
    private static readonly FilterDefinitionBuilder<MeterRollupDocument> Filter = Builders<MeterRollupDocument>.Filter;

    private readonly MongoLoggingOptions _options = options.Value;

    // Ensured on first use and flagged only after a success, as in DesktopTelemetryStore.
    private int _indexesEnsured;

    private IMongoCollection<MeterRollupDocument> Rollups =>
        field ??= context.Database.GetCollection<MeterRollupDocument>(_options.MeterRollupsCollection);

    public async Task<int> WriteAsync(IReadOnlyCollection<MeterRollup> rollups, CancellationToken ct)
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
        catch (MongoBulkWriteException<MeterRollupDocument> ex)
            when (ex.WriteErrors.All(error => error.Code == DuplicateKeyCode))
        {
            // Two containers inserted the same minute at once and the unique index let one
            // through. Retried once: the upsert now finds that document and adds to it.
            var retry = ex.WriteErrors.Select(error => writes[error.Index]).ToList();
            await Rollups.BulkWriteAsync(retry, UnorderedBulk, ct);
        }

        return writes.Count;
    }

    public async Task<MeterRollupWindow> GetWindowAsync(string meter, DateTime sinceUtc, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return new MeterRollupWindow(sinceUtc, [], null);
        }

        var pipeline = PipelineDefinition<MeterRollupDocument, BsonDocument>.Create(
        [
            new BsonDocument("$match", new BsonDocument
            {
                { "meter", meter },
                { "bucketStartUtc", new BsonDocument("$gte", sinceUtc) }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "instrument", "$instrument" }, { "seriesKey", "$seriesKey" } } },
                { "tags", new BsonDocument("$last", "$tags") },
                { "kind", new BsonDocument("$last", "$kind") },
                { "unit", new BsonDocument("$last", "$unit") },
                { "description", new BsonDocument("$last", "$description") },
                { "count", new BsonDocument("$sum", "$count") },
                { "sum", new BsonDocument("$sum", "$sum") },
                { "max", new BsonDocument("$max", "$max") },
                { "lastRecordedAtUtc", new BsonDocument("$max", "$lastRecordedAtUtc") }
            })
        ]);

        var groups = await Rollups.Aggregate(pipeline, cancellationToken: ct).ToListAsync(ct);

        var oldest = await Rollups
            .Find(Filter.Eq(doc => doc.Meter, meter))
            .SortBy(doc => doc.BucketStartUtc)
            .Limit(1)
            .Project(doc => (DateTime?)doc.BucketStartUtc)
            .FirstOrDefaultAsync(ct);

        return new MeterRollupWindow(
            sinceUtc,
            groups.Select(ToSeries).ToList(),
            oldest is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null);
    }

    private UpdateOneModel<MeterRollupDocument> ToUpsert(MeterRollup rollup)
    {
        var filter = Filter.And(
            Filter.Eq(doc => doc.BucketStartUtc, rollup.BucketStartUtc),
            Filter.Eq(doc => doc.Meter, rollup.Meter),
            Filter.Eq(doc => doc.Instrument, rollup.Instrument),
            Filter.Eq(doc => doc.SeriesKey, MeterRollup.SeriesKeyOf(rollup.Tags)));

        // The key fields come from the filter on insert, so they are not in the update.
        var updates = new List<UpdateDefinition<MeterRollupDocument>>
        {
            Update.Inc(doc => doc.Count, rollup.Count),
            Update.Inc(doc => doc.Sum, rollup.Sum),
            Update.Max(doc => doc.Max, rollup.Max),
            Update.Max(doc => doc.LastRecordedAtUtc, rollup.LastRecordedAtUtc),
            Update.SetOnInsert(doc => doc.Tags, rollup.Tags.ToDictionary(StringComparer.Ordinal)),
            Update.Set(doc => doc.Kind, rollup.Kind)
        };

        if (rollup.Unit is { Length: > 0 } unit)
        {
            updates.Add(Update.Set(doc => doc.Unit, unit));
        }

        if (rollup.Description is { Length: > 0 } description)
        {
            updates.Add(Update.Set(doc => doc.Description, description));
        }

        if (_options.ProcessName is { Length: > 0 } processName)
        {
            updates.Add(Update.Set(doc => doc.ProcessName, processName));
        }

        return new UpdateOneModel<MeterRollupDocument>(filter, Update.Combine(updates)) { IsUpsert = true };
    }

    private static MeterSeriesTotal ToSeries(BsonDocument group)
    {
        var id = group["_id"].AsBsonDocument;
        var tags = group["tags"] is BsonDocument tagDocument
            ? tagDocument.ToDictionary(element => element.Name, element => element.Value.ToString() ?? string.Empty)
            : [];

        return new MeterSeriesTotal(
            id["instrument"].AsString,
            OptionalString(group, "kind") ?? string.Empty,
            OptionalString(group, "unit"),
            OptionalString(group, "description"),
            tags,
            group["count"].ToInt64(),
            group["sum"].ToDouble(),
            group["max"].ToDouble(),
            group["lastRecordedAtUtc"].ToUniversalTime());
    }

    private static string? OptionalString(BsonDocument document, string name)
        => document.TryGetValue(name, out var value) && value.IsString ? value.AsString : null;

    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) != 0)
        {
            return;
        }

        var models = new List<CreateIndexModel<MeterRollupDocument>>
        {
            // The upsert key. Unique, so two ingestor containers — the two lanes run as two —
            // cannot split a minute into duplicate documents the read would then double.
            new(Builders<MeterRollupDocument>.IndexKeys
                    .Ascending(doc => doc.BucketStartUtc)
                    .Ascending(doc => doc.Meter)
                    .Ascending(doc => doc.Instrument)
                    .Ascending(doc => doc.SeriesKey),
                new CreateIndexOptions { Name = "ux_bucket_meter_instrument_series", Unique = true }),
            // Every read is one meter over "the last N": the window scan and the oldest-bucket
            // lookup both start here.
            new(Builders<MeterRollupDocument>.IndexKeys
                    .Ascending(doc => doc.Meter)
                    .Ascending(doc => doc.BucketStartUtc),
                new CreateIndexOptions { Name = "ix_meter_bucket" })
        };

        await Rollups.Indexes.CreateManyAsync(models, ct);
        await MongoIndexes.ReconcileTtlAsync(Rollups, doc => doc.BucketStartUtc, _options.MeterRollupsRetention, ct);

        Volatile.Write(ref _indexesEnsured, 1);
    }
}
