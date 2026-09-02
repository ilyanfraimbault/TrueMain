using Data.Logging.Mongo;
using MongoDB.Driver;

namespace Data.Metrics.Mongo;

/// <summary>
/// Mongo adapter for the hourly candidate-stock snapshots (#1403), the same
/// direct-call shape as <see cref="DbStorageSnapshotStore"/>: one write per pipeline
/// run, driven by an ingestor step rather than by a channel.
/// </summary>
internal sealed class CandidateStockSnapshotStore(MongoLogContext context) : ICandidateStockSnapshotStore
{
    private static readonly BulkWriteOptions UnorderedBulk = new() { IsOrdered = false };

    // Same first-use index bootstrap as DbStorageSnapshotStore: this store has no sink
    // to hang index creation off. Only set after a success, so a transient Mongo
    // failure retries next run rather than leaving the collection unindexed forever.
    private int _indexesEnsured;

    public async Task<int> UpsertHourAsync(
        DateTime capturedAtUtc,
        IReadOnlyList<CandidateStockSample> samples,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // Mongo is optional in every environment, so a missing connection must cost
        // the caller nothing rather than throwing through the pipeline.
        if (!context.IsActive || samples.Count == 0)
        {
            return 0;
        }

        if (Volatile.Read(ref _indexesEnsured) == 0)
        {
            await context.EnsureCandidateStockSnapshotIndexesAsync(ct);
            Volatile.Write(ref _indexesEnsured, 1);
        }

        var hour = TruncateToHour(capturedAtUtc);

        var writes = new List<WriteModel<CandidateStockSnapshotDocument>>(samples.Count);
        foreach (var sample in samples)
        {
            var filter = Builders<CandidateStockSnapshotDocument>.Filter.And(
                Builders<CandidateStockSnapshotDocument>.Filter.Eq(doc => doc.SnapshotHourUtc, hour),
                Builders<CandidateStockSnapshotDocument>.Filter.Eq(doc => doc.PlatformId, sample.PlatformId),
                Builders<CandidateStockSnapshotDocument>.Filter.Eq(doc => doc.Status, sample.Status));

            // $set, not $inc: a snapshot is an absolute reading, and the run that
            // writes it last in an hour is the one that should win. The key fields come
            // from the filter on insert, so setting them here too would conflict.
            var update = Builders<CandidateStockSnapshotDocument>.Update
                .Set(doc => doc.Count, sample.Count)
                .Set(doc => doc.CapturedAtUtc, capturedAtUtc);

            writes.Add(new UpdateOneModel<CandidateStockSnapshotDocument>(filter, update) { IsUpsert = true });
        }

        await context.CandidateStockSnapshots.BulkWriteAsync(writes, UnorderedBulk, ct);
        return writes.Count;
    }

    public async Task<IReadOnlyList<CandidateStockSnapshotPoint>> GetHistoryAsync(
        DateTime sinceUtc,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        var since = TruncateToHour(sinceUtc);

        // Ordered oldest-first so the read side can walk the hours in order without
        // re-sorting; ix_snapshot_hour_desc serves the bound.
        var documents = await context.CandidateStockSnapshots
            .Find(doc => doc.SnapshotHourUtc >= since)
            .SortBy(doc => doc.SnapshotHourUtc)
            .ThenBy(doc => doc.PlatformId)
            .ThenBy(doc => doc.Status)
            .ToListAsync(ct);

        return documents
            .Select(doc => new CandidateStockSnapshotPoint(
                DateTime.SpecifyKind(doc.SnapshotHourUtc, DateTimeKind.Utc),
                doc.PlatformId,
                doc.Status,
                doc.Count))
            .ToList();
    }

    private static DateTime TruncateToHour(DateTime instant)
        => new(instant.Year, instant.Month, instant.Day, instant.Hour, 0, 0, DateTimeKind.Utc);
}
