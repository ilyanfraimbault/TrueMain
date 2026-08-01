namespace Data.Metrics.Mongo;

/// <summary>
/// Write and read access to the daily Postgres storage snapshots (#925). Both halves
/// sit on one interface because they are two ends of the same tiny collection and
/// splitting them would mean two near-identical Mongo adapters.
/// </summary>
public interface IDbStorageSnapshotStore
{
    /// <summary>
    /// Upserts one document per table for <paramref name="snapshotDateUtc"/>, replacing
    /// whatever that day already held. Idempotent by design: the ingestor pipeline runs
    /// many times a day, and each run simply refreshes the day's reading rather than
    /// appending a new point.
    /// </summary>
    /// <remarks>
    /// <paramref name="databaseBytes"/> is measured (<c>pg_database_size</c>), not summed
    /// from <paramref name="samples"/>: the sum only covers <c>public</c>-schema user
    /// tables, whereas the forecast has to project the number that actually fills the
    /// volume. See <see cref="DbTableSizeSnapshotDocument.DatabaseBytes"/>.
    /// </remarks>
    /// <returns>Documents written, or 0 when Mongo is not configured.</returns>
    Task<int> UpsertDayAsync(
        DateTime snapshotDateUtc,
        long databaseBytes,
        IReadOnlyList<DbTableSizeSample> samples,
        CancellationToken ct);

    /// <summary>
    /// Every snapshot at or after <paramref name="sinceUtc"/>, oldest first. Returns an
    /// empty list when Mongo is not configured, so the admin panel degrades to "no
    /// history yet" instead of failing.
    /// </summary>
    Task<IReadOnlyList<DbTableSizeSnapshotPoint>> GetHistoryAsync(DateTime sinceUtc, CancellationToken ct);
}

/// <summary>One table's measurement, as read from <c>pg_catalog</c> before persisting.</summary>
public sealed record DbTableSizeSample(
    string TableName,
    long RowEstimate,
    long TotalBytes,
    long TableBytes,
    long IndexBytes);

/// <summary>One persisted point of the history: a table's footprint on a given day.</summary>
public sealed record DbTableSizeSnapshotPoint(
    DateTime SnapshotDateUtc,
    string TableName,
    long RowEstimate,
    long TotalBytes,
    long TableBytes,
    long IndexBytes,
    long DatabaseBytes);
