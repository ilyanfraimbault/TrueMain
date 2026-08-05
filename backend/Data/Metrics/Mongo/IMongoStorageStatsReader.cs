namespace Data.Metrics.Mongo;

/// <summary>
/// Reads MongoDB's own storage footprint — per collection and for the database as a
/// whole (#1023).
///
/// <para>
/// It exists because the database panel and its disk forecast measured Postgres only,
/// while Mongo holds the logs, crashes, audit events, Riot rollups, process runs and
/// seed requests on the <em>same</em> volume. Two of those collections have no TTL at
/// all, so the ones most likely to surprise us were exactly the ones nothing watched.
/// </para>
/// </summary>
public interface IMongoStorageStatsReader
{
    /// <summary>
    /// Current sizes of every collection in the logging database, plus the database
    /// total. Returns <see langword="null"/> when Mongo is not configured, so callers
    /// render "not measured" rather than a zero that would read as "empty".
    /// </summary>
    Task<MongoStorageStats?> GetAsync(CancellationToken ct);
}

/// <summary>
/// A Mongo storage reading: the per-collection samples and the database-wide total.
/// </summary>
/// <param name="DatabaseBytes">
/// <c>dbStats.storageSize + dbStats.indexSize</c> — what the database occupies on
/// disk, not <c>dataSize</c> (which is the uncompressed logical size and runs several
/// times larger than the files). The forecast projects the number that fills the
/// volume, so it has to be the on-disk one.
/// </param>
/// <param name="Collections">
/// One sample per collection in the database, name-ordered. Covers every collection
/// present, not just the ones this codebase models, so a collection created outside
/// it still shows up on the panel.
/// </param>
public sealed record MongoStorageStats(
    long DatabaseBytes,
    IReadOnlyList<DbTableSizeSample> Collections);
