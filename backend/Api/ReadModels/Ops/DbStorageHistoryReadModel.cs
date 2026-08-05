namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Storage growth over a window, plus the disk-exhaustion forecast (#925). Read
/// entirely from the daily snapshot collection — the page never runs a
/// <c>pg_catalog</c> scan of its own, which is the point of snapshotting.
/// </summary>
public sealed record DbStorageHistoryReadModel
{
    /// <summary>Days covered, oldest first. Empty until the snapshot step has run.</summary>
    public IReadOnlyList<DbStorageDailyPoint> Daily { get; init; } = [];

    /// <summary>
    /// The largest tables, each with its own series. Capped by
    /// <c>StorageHistory:TopTables</c>; smaller tables still count towards
    /// <see cref="DbStorageDailyPoint.TotalBytes"/>, they just are not charted.
    /// </summary>
    public IReadOnlyList<DbStorageTableSeries> Tables { get; init; } = [];

    /// <summary>
    /// The storage engines the window actually contains readings for — <c>postgres</c>,
    /// <c>mongo</c>, or both (#1023). The panel states it rather than implying the
    /// figures are the whole disk: before the first Mongo snapshot lands, and in any
    /// environment where Mongo is unconfigured, the totals cover Postgres only.
    /// </summary>
    public IReadOnlyList<string> Engines { get; init; } = [];

    /// <summary>
    /// Null when no projection can honestly be made: fewer than three days of
    /// history, flat or shrinking storage, or no configured disk capacity. Never a
    /// placeholder — the panel shows why instead.
    /// </summary>
    public DbStorageForecast? Forecast { get; init; }
}

/// <summary>One day's totals across the whole database.</summary>
public sealed record DbStorageDailyPoint
{
    public DateTime DateUtc { get; init; }

    /// <summary>
    /// What actually occupies the volume that day: measured <c>pg_database_size</c>
    /// (catalogs included) plus Mongo's own on-disk size, summed because both engines
    /// share one volume (#1023). This, not <see cref="TotalBytes"/>, is what the
    /// forecast projects.
    /// </summary>
    public long DatabaseBytes { get; init; }

    /// <summary>
    /// The Postgres half of <see cref="DatabaseBytes"/>, so the panel can show the
    /// split instead of asserting one opaque total. 0 when the day has no Postgres
    /// reading.
    /// </summary>
    public long PostgresBytes { get; init; }

    /// <summary>
    /// The Mongo half of <see cref="DatabaseBytes"/>. 0 for every day before #1023
    /// shipped and in environments where Mongo is unconfigured — which is why the
    /// forecast only fits days measuring the same engines as the latest one.
    /// </summary>
    public long MongoBytes { get; init; }

    /// <summary>
    /// Sum of the per-object sizes: <c>pg_total_relation_size</c> over
    /// <c>public</c>-schema tables, plus each Mongo collection's storage + indexes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>Sum of the planner's live-tuple estimates. A trend figure, not an audit count.</summary>
    public long RowEstimate { get; init; }
}

/// <summary>One table's growth over the window.</summary>
public sealed record DbStorageTableSeries
{
    /// <summary>Which engine owns the object: <c>postgres</c> or <c>mongo</c>.</summary>
    public string Engine { get; init; } = string.Empty;

    public string TableName { get; init; } = string.Empty;

    public IReadOnlyList<DbStorageTablePoint> Points { get; init; } = [];

    /// <summary>Most recent measured size.</summary>
    public long CurrentBytes { get; init; }

    /// <summary>
    /// Average bytes added per day across the window (first to last observation,
    /// divided by the days between them). Negative when the table shrank.
    /// </summary>
    public long BytesPerDay { get; init; }

    /// <summary>Average rows added per day, on the same first-to-last basis.</summary>
    public long RowsPerDay { get; init; }

    /// <summary>
    /// Growth over the window as a fraction of the starting size (0.25 = +25%). Null
    /// when the table started the window at zero bytes — the growth is then undefined
    /// rather than infinite, and the absolute figures already say what happened.
    /// </summary>
    public double? GrowthRate { get; init; }
}

/// <summary>One table's size on one day.</summary>
public sealed record DbStorageTablePoint
{
    public DateTime DateUtc { get; init; }

    public long TotalBytes { get; init; }

    public long RowEstimate { get; init; }
}

/// <summary>The fitted growth rate and when it reaches each configured fill level.</summary>
public sealed record DbStorageForecast
{
    /// <summary>Least-squares slope of database size against time, in bytes per day.</summary>
    public long BytesPerDay { get; init; }

    /// <summary>The configured volume size the thresholds are percentages of.</summary>
    public long DiskCapacityBytes { get; init; }

    public IReadOnlyList<DbStorageThresholdCrossing> Crossings { get; init; } = [];
}

/// <summary>When the fitted line reaches one fill level.</summary>
public sealed record DbStorageThresholdCrossing
{
    /// <summary>Fill level as a percentage of the disk (80, 90, 100…).</summary>
    public double Percent { get; init; }

    public long ThresholdBytes { get; init; }

    /// <summary>
    /// Projected crossing date. Null when it lands more than a century away in either
    /// direction — no meaningful date at this rate, rather than a spurious one. A date
    /// in the past means the level is already breached.
    /// </summary>
    public DateTime? ProjectedAtUtc { get; init; }
}
