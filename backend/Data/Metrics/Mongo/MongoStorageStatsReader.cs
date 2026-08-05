using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <inheritdoc />
public sealed class MongoStorageStatsReader(MongoLogContext context) : IMongoStorageStatsReader
{
    public async Task<MongoStorageStats?> GetAsync(CancellationToken ct)
    {
        // Mongo is optional in every environment, and the whole logging stack degrades
        // to no-op when unconfigured — so an absent connection is "not measured", not
        // an error and not a zero.
        if (!context.IsActive)
        {
            return null;
        }

        var database = context.Database;

        // storageSize + indexSize, not dataSize: dataSize is the uncompressed logical
        // size and runs several times larger than the files on disk. The forecast has
        // to project what actually fills the volume.
        var dbStats = await database.RunCommandAsync(
            new BsonDocumentCommand<BsonDocument>(new BsonDocument("dbStats", 1)),
            cancellationToken: ct);

        var databaseBytes = ReadInt64(dbStats, "storageSize") + ReadInt64(dbStats, "indexSize");

        using var namesCursor = await database.ListCollectionNamesAsync(cancellationToken: ct);
        var names = await namesCursor.ToListAsync(ct);
        names.Sort(StringComparer.Ordinal);

        var collections = new List<DbTableSizeSample>(names.Count);
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();

            var stats = await ReadCollectionStatsAsync(database, name, ct);
            if (stats is null)
            {
                continue;
            }

            collections.Add(stats);
        }

        return new MongoStorageStats(databaseBytes, collections);
    }

    /// <summary>
    /// One collection's storage stats via the <c>$collStats</c> aggregation stage.
    /// The <c>collStats</c> <em>command</em> would be the obvious equivalent of
    /// Postgres' catalog query, but it has been deprecated since MongoDB 6.2 and this
    /// deployment runs 8.0.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the object has no storage stats to report — a view,
    /// for instance, which <c>$collStats</c> answers for without a
    /// <c>storageStats</c> document. Skipped rather than counted as zero bytes.
    /// </returns>
    private static async Task<DbTableSizeSample?> ReadCollectionStatsAsync(
        IMongoDatabase database,
        string name,
        CancellationToken ct)
    {
        var pipeline = new BsonDocument[]
        {
            new("$collStats", new BsonDocument("storageStats", new BsonDocument()))
        };

        using var cursor = await database
            .GetCollection<BsonDocument>(name)
            .AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct);

        var document = await cursor.FirstOrDefaultAsync(ct);

        if (document is null
            || !document.TryGetValue("storageStats", out var raw)
            || raw is not BsonDocument stats)
        {
            return null;
        }

        var storageBytes = ReadInt64(stats, "storageSize");
        var indexBytes = ReadInt64(stats, "totalIndexSize");

        return new DbTableSizeSample(
            TableName: name,
            // Exact, unlike the Postgres side's n_live_tup planner estimate — worth
            // knowing when the two sit in the same column.
            RowEstimate: ReadInt64(stats, "count"),
            TotalBytes: storageBytes + indexBytes,
            TableBytes: storageBytes,
            IndexBytes: indexBytes);
    }

    /// <summary>
    /// Reads a numeric field defensively: Mongo returns these as Int32 or Int64
    /// depending on magnitude, and a missing field means the server did not report it
    /// rather than that the value is meaningfully zero — but zero is the only sane
    /// contribution to a sum, so that is what an absent field yields.
    /// </summary>
    private static long ReadInt64(BsonDocument document, string field)
        => document.TryGetValue(field, out var value) && value.IsNumeric ? value.ToInt64() : 0;
}
