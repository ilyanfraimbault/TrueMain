using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// One day's storage footprint for one Postgres table, persisted in the
/// <c>db_table_size_snapshots</c> collection (#925). Written by the Ingestor's
/// storage-snapshot step (a day-keyed upsert per table) and read back by the admin
/// database panel for the growth charts and the disk-exhaustion forecast.
///
/// <para>
/// Same shape as <see cref="RiotApiCallRollupDocument"/> (#583), which the issue
/// names as the template, and in Mongo for the same reason the other metrics are:
/// append-only, time-ordered, ops-only, no relational joins, and a native TTL index
/// prunes it for free instead of needing its own retention arm.
/// </para>
///
/// <para>
/// <b>Daily granularity, last write wins.</b> Prod runs the ingestor with
/// <c>RunOnce</c> plus <c>restart: unless-stopped</c>, so the pipeline re-runs
/// back-to-back many times a day. The document is keyed on
/// <see cref="SnapshotDateUtc"/> (midnight UTC) rather than the wall clock, so those
/// repeated runs overwrite the day's reading instead of accumulating one row per run
/// — the series stays exactly one point per table per day, carrying the most recent
/// measurement of that day.
/// </para>
/// </summary>
public sealed class DbTableSizeSnapshotDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The snapshot's day, truncated to midnight UTC. Also the TTL field.</summary>
    [BsonElement("snapshotDateUtc")]
    public DateTime SnapshotDateUtc { get; set; }

    [BsonElement("tableName")]
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Planner live-tuple estimate (<c>pg_stat_user_tables.n_live_tup</c>), not an
    /// exact count — the same caveat the live panel carries, and the reason the
    /// derived "rows per day" is a trend indicator rather than an audit figure. Can
    /// read 0 until the table has been analysed.
    /// </summary>
    [BsonElement("rowEstimate")]
    public long RowEstimate { get; set; }

    /// <summary>Heap + indexes + TOAST (<c>pg_total_relation_size</c>).</summary>
    [BsonElement("totalBytes")]
    public long TotalBytes { get; set; }

    [BsonElement("tableBytes")]
    public long TableBytes { get; set; }

    [BsonElement("indexBytes")]
    public long IndexBytes { get; set; }

    /// <summary>
    /// <c>pg_database_size(current_database())</c> at snapshot time, denormalised onto
    /// every row of the day. Deliberately not derived by summing
    /// <see cref="TotalBytes"/>: that sum only covers <c>public</c>-schema user tables,
    /// while the disk-full incident this feature exists for (#680) was about the volume
    /// — catalogs, WAL-adjacent files and everything else included. The forecast has to
    /// project the number that actually fills the disk, so it is measured rather than
    /// reconstructed. Eight bytes × ~60 tables × one row per day is not worth a second
    /// collection to normalise away.
    /// </summary>
    [BsonElement("databaseBytes")]
    public long DatabaseBytes { get; set; }

    /// <summary>Wall-clock time the reading was taken, for "last updated" display.</summary>
    [BsonElement("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; set; }
}
