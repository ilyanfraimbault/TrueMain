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
    /// Measured <c>pg_database_size</c> — what actually occupies the volume, including
    /// catalogs. This, not <see cref="TotalBytes"/>, is what the forecast projects.
    /// </summary>
    public long DatabaseBytes { get; init; }

    /// <summary>Sum of <c>pg_total_relation_size</c> over <c>public</c>-schema tables.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Sum of the planner's live-tuple estimates. A trend figure, not an audit count.</summary>
    public long RowEstimate { get; init; }
}

/// <summary>One table's growth over the window.</summary>
public sealed record DbStorageTableSeries
{
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
    /// Projected crossing date. Null when it is more than a century out — "not
    /// foreseeable at this rate" rather than a spurious far-future date. A date in the
    /// past means the level is already breached.
    /// </summary>
    public DateTime? ProjectedAtUtc { get; init; }
}
