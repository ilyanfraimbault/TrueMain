namespace TrueMain.Options;

/// <summary>
/// Knobs for the admin storage-growth panel (#925). Ops values rather than product
/// ones, but they bind the same way — from <c>StorageHistory:*</c> — so the disk
/// capacity can be corrected after a volume resize without a redeploy.
/// </summary>
public sealed class StorageHistoryOptions
{
    public const string SectionName = "StorageHistory";

    /// <summary>
    /// Size of the volume Postgres lives on. There is no portable way to ask the
    /// database how big its disk is — <c>pg_database_size</c> reports what the database
    /// uses, not what the filesystem holds — so the capacity has to be configured.
    /// Left at 0 (the default) the panel still charts growth and the daily rate but
    /// offers no threshold forecast, which is the correct behaviour for an environment
    /// nobody has told the real figure to: a forecast against a made-up capacity would
    /// be worse than none.
    /// </summary>
    public long DiskCapacityBytes { get; set; }

    /// <summary>
    /// Fill levels, as percentages of <see cref="DiskCapacityBytes"/>, the forecast
    /// projects a crossing date for. Defaults to the two levels worth acting on plus
    /// the wall itself: 80% is "plan something", 90% is "do it now", 100% is the #680
    /// incident repeating.
    /// </summary>
    public double[] ThresholdPercents { get; set; } = [80, 90, 100];

    /// <summary>
    /// Days of history the panel reads and fits by default. A quarter is long enough
    /// for the trend to survive a patch cycle's ingestion bump and short enough that a
    /// volume resize six months ago does not drag the line.
    /// </summary>
    public int DefaultWindowDays { get; set; } = 90;

    /// <summary>
    /// How many tables get their own series in the response, largest first. The rest
    /// are still counted in the totals — they are simply not charted individually,
    /// because ~60 lines on one chart is not a chart.
    /// </summary>
    public int TopTables { get; set; } = 10;
}
