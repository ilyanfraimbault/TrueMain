using Data.Logging.Mongo;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Mongo adapter for the daily storage snapshots (#925), modelled on
/// <see cref="RiotApiMetricsSink"/>'s upsert shape but driven by a caller rather than
/// a channel — there is one write per pipeline run, not a stream.
/// </summary>
internal sealed class DbStorageSnapshotStore(MongoLogContext context) : IDbStorageSnapshotStore
{
    private static readonly BulkWriteOptions UnorderedBulk = new() { IsOrdered = false };

    public async Task<int> UpsertDayAsync(
        DateTime snapshotDateUtc,
        long databaseBytes,
        IReadOnlyList<DbTableSizeSample> samples,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // Mongo is optional in every environment (the whole logging stack degrades to
        // no-op when unconfigured), so a missing connection must cost the caller
        // nothing rather than throwing through the pipeline.
        if (!context.IsActive || samples.Count == 0)
        {
            return 0;
        }

        await context.EnsureDbTableSizeSnapshotIndexesAsync(ct);

        var day = DateTime.SpecifyKind(snapshotDateUtc.Date, DateTimeKind.Utc);
        var capturedAtUtc = snapshotDateUtc;

        var writes = new List<WriteModel<DbTableSizeSnapshotDocument>>(samples.Count);
        foreach (var sample in samples)
        {
            var filter = Builders<DbTableSizeSnapshotDocument>.Filter.And(
                Builders<DbTableSizeSnapshotDocument>.Filter.Eq(doc => doc.SnapshotDateUtc, day),
                Builders<DbTableSizeSnapshotDocument>.Filter.Eq(doc => doc.TableName, sample.TableName));

            // $set, not $inc: a snapshot is an absolute reading, and the run that
            // writes it last in a day is the one that should win. The key fields come
            // from the filter on insert, so setting them here too would conflict with
            // the filter-implied values.
            var update = Builders<DbTableSizeSnapshotDocument>.Update
                .Set(doc => doc.RowEstimate, sample.RowEstimate)
                .Set(doc => doc.TotalBytes, sample.TotalBytes)
                .Set(doc => doc.TableBytes, sample.TableBytes)
                .Set(doc => doc.IndexBytes, sample.IndexBytes)
                .Set(doc => doc.DatabaseBytes, databaseBytes)
                .Set(doc => doc.CapturedAtUtc, capturedAtUtc);

            writes.Add(new UpdateOneModel<DbTableSizeSnapshotDocument>(filter, update) { IsUpsert = true });
        }

        await context.DbTableSizeSnapshots.BulkWriteAsync(writes, UnorderedBulk, ct);
        return writes.Count;
    }

    public async Task<IReadOnlyList<DbTableSizeSnapshotPoint>> GetHistoryAsync(
        DateTime sinceUtc,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        var since = DateTime.SpecifyKind(sinceUtc.Date, DateTimeKind.Utc);

        // Ordered oldest-first so the read side can walk consecutive days to derive
        // per-day deltas without re-sorting; ix_snapshot_date_desc serves the bound.
        var documents = await context.DbTableSizeSnapshots
            .Find(doc => doc.SnapshotDateUtc >= since)
            .SortBy(doc => doc.SnapshotDateUtc)
            .ThenBy(doc => doc.TableName)
            .ToListAsync(ct);

        return documents
            .Select(doc => new DbTableSizeSnapshotPoint(
                DateTime.SpecifyKind(doc.SnapshotDateUtc, DateTimeKind.Utc),
                doc.TableName,
                doc.RowEstimate,
                doc.TotalBytes,
                doc.TableBytes,
                doc.IndexBytes,
                doc.DatabaseBytes))
            .ToList();
    }
}
