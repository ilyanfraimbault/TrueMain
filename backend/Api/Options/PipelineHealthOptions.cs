namespace TrueMain.Options;

/// <summary>
/// Thresholds for the operator cockpit (#1031), bound from <c>PipelineHealth:*</c>.
///
/// <para>
/// Deliberately two knobs. Every signal the cockpit shows that already has a panel of its
/// own is judged by <em>that</em> panel's thresholds — the data-quality detectors keep
/// <c>DataQualityDetectors:*</c> (including the newest-match-age and queue-depth levels
/// behind the ingestion-lag tile), the storage panel keeps <c>StorageHistory:*</c>. Adding a
/// knob here is a decision to let the cockpit disagree with the page a tile links to, so it
/// needs a reason; the two below have one.
/// </para>
/// </summary>
public sealed class PipelineHealthOptions
{
    public const string SectionName = "PipelineHealth";

    /// <summary>
    /// Days until the nearest projected disk-fill crossing at which the tile turns amber.
    /// <c>StorageHistory:ThresholdPercents</c> configures <em>which</em> fill levels get a
    /// projected date; it says nothing about when a date is close enough to act on, so that
    /// call is made here.
    /// </summary>
    public double DiskForecastAmberDays { get; set; } = 90;

    /// <summary>
    /// Days until the nearest projected crossing at which the tile turns red. Set either
    /// level to 0 or less to disable it.
    /// </summary>
    public double DiskForecastRedDays { get; set; } = 30;
}
