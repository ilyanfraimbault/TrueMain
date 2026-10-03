namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The admin portal's view of the desktop app (#1805): installers downloaded from the site and
/// what the installed apps did, over a chosen window of UTC days ending today.
///
/// <para>
/// <b>Measured from the first day anything was recorded, never before.</b> The series starts at
/// <see cref="EarliestDayUtc"/>: the app did not report anything before the version that sends
/// telemetry, and those days were unmeasured, not empty.
/// </para>
/// </summary>
public sealed record DesktopUsageReadModel
{
    /// <summary>The requested window in days, after clamping; today included.</summary>
    public int WindowDays { get; init; }

    /// <summary>The usage collection's TTL in days: no window reaches further back than this.</summary>
    public int UsageRetentionDays { get; init; }

    /// <summary>The first day with a recorded download or usage in the window, or null when there is none.</summary>
    public DateTime? EarliestDayUtc { get; init; }

    /// <summary>Distinct installs seen today, over the last 7 days and the last 30, whatever the window.</summary>
    public DesktopActiveInstalls Active { get; init; } = new();

    /// <summary>Sums over the window.</summary>
    public DesktopUsageTotals Totals { get; init; } = new();

    /// <summary>One bucket per day from <see cref="EarliestDayUtc"/> to today, oldest first.</summary>
    public IReadOnlyList<DesktopUsageDayBucket> Days { get; init; } = [];

    /// <summary>Installs active in the window, by the version of their latest day, most installs first.</summary>
    public IReadOnlyList<DesktopShare> Versions { get; init; } = [];

    /// <summary>Installs active in the window, by operating system, most installs first.</summary>
    public IReadOnlyList<DesktopShare> OperatingSystems { get; init; } = [];

    /// <summary>Downloads in the window by version, newest first by first download day.</summary>
    public IReadOnlyList<DesktopDownloadVersion> DownloadVersions { get; init; } = [];

    /// <summary>Every page the app counts, used or not, most viewed first.</summary>
    public IReadOnlyList<DesktopKeyUsage> Pages { get; init; } = [];

    /// <summary>Every feature the app counts, used or not, most used first.</summary>
    public IReadOnlyList<DesktopKeyUsage> Features { get; init; } = [];
}

public sealed record DesktopActiveInstalls
{
    public int Today { get; init; }

    public int Last7Days { get; init; }

    public int Last30Days { get; init; }
}

public sealed record DesktopUsageTotals
{
    /// <summary>Distinct installs that reported at least once in the window.</summary>
    public int Installs { get; init; }

    /// <summary>Installs whose first launch fell in the window.</summary>
    public int NewInstalls { get; init; }

    public long Launches { get; init; }

    public long OpenMinutes { get; init; }

    /// <summary>Install-days in the window: the denominator of the time open per active day.</summary>
    public int ActiveInstallDays { get; init; }

    public long DownloadsMac { get; init; }

    public long DownloadsWindows { get; init; }
}

public sealed record DesktopUsageDayBucket
{
    public DateTime DayUtc { get; init; }

    public int ActiveInstalls { get; init; }

    public int NewInstalls { get; init; }

    public long Launches { get; init; }

    public long OpenMinutes { get; init; }

    public long DownloadsMac { get; init; }

    public long DownloadsWindows { get; init; }
}

/// <summary>A count of installs behind one value — a version, an operating system.</summary>
public sealed record DesktopShare
{
    public string Key { get; init; } = string.Empty;

    public int Installs { get; init; }
}

public sealed record DesktopDownloadVersion
{
    public string Version { get; init; } = string.Empty;

    public long Mac { get; init; }

    public long Windows { get; init; }
}

/// <summary>One page or feature: how often it was used in the window, and by how many installs.</summary>
public sealed record DesktopKeyUsage
{
    public string Key { get; init; } = string.Empty;

    public long Count { get; init; }

    public int Installs { get; init; }
}
