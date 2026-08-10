using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// One day's storage footprint for one Postgres table or one Mongo collection,
/// persisted in the
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

    /// <summary>
    /// Which engine the row measures — <c>postgres</c> or <c>mongo</c> (see
    /// <see cref="StorageEngines"/>). Part of the day-keyed upsert key, because the
    /// two engines genuinely share names: <c>process_runs</c> and
    /// <c>seed_requests</c> exist as both a (frozen) Postgres table and a Mongo
    /// collection, so without the discriminator one would silently overwrite the
    /// other's reading every day.
    ///
    /// <para>
    /// Documents written before #1023 carry no field at all; they are Postgres rows
    /// by construction, which is why the property defaults to <c>postgres</c> rather
    /// than to empty — an old document deserialises as what it actually measured.
    /// </para>
    /// </summary>
    [BsonElement("engine")]
    public string Engine { get; set; } = StorageEngines.Postgres;

    /// <summary>The Postgres table name, or the Mongo collection name.</summary>
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

    /// <summary>
    /// Everything the object costs on disk: heap + indexes + TOAST
    /// (<c>pg_total_relation_size</c>) for Postgres, compressed storage + indexes
    /// (<c>$collStats.storageStats</c>) for Mongo.
    /// </summary>
    [BsonElement("totalBytes")]
    public long TotalBytes { get; set; }

    /// <summary>The object itself, without its indexes.</summary>
    [BsonElement("tableBytes")]
    public long TableBytes { get; set; }

    [BsonElement("indexBytes")]
    public long IndexBytes { get; set; }

    /// <summary>
    /// The whole engine's footprint at snapshot time, denormalised onto every row of
    /// the day for that engine: <c>pg_database_size(current_database())</c> for
    /// Postgres, <c>dbStats</c> storage + index size for Mongo. Deliberately not
    /// derived by summing
    /// <see cref="TotalBytes"/>: that sum only covers <c>public</c>-schema user tables,
    /// while the disk-full incident this feature exists for (#680) was about the volume
    /// — catalogs, WAL-adjacent files and everything else included. The forecast has to
    /// project the number that actually fills the disk, so it is measured rather than
    /// reconstructed. Eight bytes × ~60 tables × one row per day is not worth a second
    /// collection to normalise away.
    ///
    /// <para>
    /// It is per engine, so the disk total is the sum across engines for the day,
    /// never the max — both engines sit on the same volume (verified on prod: one
    /// <c>/dev/sda1</c> under <c>/var/lib/docker/volumes</c>), so whichever one is
    /// larger is not the number that fills it.
    /// </para>
    /// </summary>
    [BsonElement("databaseBytes")]
    public long DatabaseBytes { get; set; }

    /// <summary>Wall-clock time the reading was taken, for "last updated" display.</summary>
    [BsonElement("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; set; }
}
