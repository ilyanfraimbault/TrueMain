namespace Data.Desktop;

/// <summary>
/// Write and read access to the desktop app's telemetry (#1805): the per-install daily usage
/// and the per-day download counters. One interface for both collections, like
/// <see cref="Data.Metrics.Mongo.ICandidateStockSnapshotStore"/>: two small ends of one
/// feature, and every method degrades to a no-op when Mongo is not configured.
/// </summary>
public interface IDesktopTelemetryStore
{
    /// <summary>
    /// Folds one batch into the install's document for the UTC day of
    /// <paramref name="receivedAtUtc"/>. The batch is already validated: page and feature keys
    /// are allow-listed identifiers.
    /// </summary>
    /// <returns>False when Mongo is not configured.</returns>
    Task<bool> RecordUsageAsync(DesktopUsageBatch batch, DateTime receivedAtUtc, CancellationToken ct);

    /// <summary>Counts one installer download for the UTC day of <paramref name="atUtc"/>.</summary>
    /// <returns>False when Mongo is not configured.</returns>
    Task<bool> RecordDownloadAsync(string platform, string version, DateTime atUtc, CancellationToken ct);

    /// <summary>Every usage day on or after <paramref name="sinceDayUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<DesktopUsageDay>> GetUsageAsync(DateTime sinceDayUtc, CancellationToken ct);

    /// <summary>Every download counter on or after <paramref name="sinceDayUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<DesktopDownloadDay>> GetDownloadsAsync(DateTime sinceDayUtc, CancellationToken ct);
}

/// <summary>What the app counted since its previous batch.</summary>
public sealed record DesktopUsageBatch(
    Guid InstallId,
    string AppVersion,
    string Os,
    bool FirstLaunch,
    int Launches,
    int OpenMinutes,
    IReadOnlyDictionary<string, int> PageViews,
    IReadOnlyDictionary<string, int> Features);

/// <summary>One install's persisted day.</summary>
public sealed record DesktopUsageDay(
    DateTime DayUtc,
    string InstallId,
    string AppVersion,
    string Os,
    bool FirstLaunch,
    int Launches,
    int OpenMinutes,
    IReadOnlyDictionary<string, int> PageViews,
    IReadOnlyDictionary<string, int> Features);

/// <summary>One persisted download counter.</summary>
public sealed record DesktopDownloadDay(DateTime DayUtc, string Platform, string Version, long Count);
