using Data.Desktop;
using Data.Logging.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Desktop;

namespace TrueMain.Services.Ops.Desktop;

public interface IDesktopUsageQueryService
{
    Task<DesktopUsageReadModel> GetAsync(int? windowDays, CancellationToken ct);
}

/// <summary>
/// Shapes the desktop telemetry (#1805) for the admin page. The store returns the raw days and
/// this does the arithmetic in memory, like <c>CandidateStockQueryService</c>: a document is
/// one install's day, so a 90-day window over a beta's installs is a few thousand small rows,
/// and the distinct counts (active installs, versions) are simpler and exactly testable here.
/// </summary>
public sealed class DesktopUsageQueryService(
    IDesktopTelemetryStore store,
    IOptions<MongoLoggingOptions> mongoOptions,
    TimeProvider timeProvider) : IDesktopUsageQueryService
{
    internal const int DefaultWindowDays = 30;

    /// <summary>
    /// The largest window the page offers. The read scans every install-day in it, so the bound
    /// is what keeps the page cheap while the install base grows.
    /// </summary>
    internal const int MaxWindowDays = 90;

    /// <summary>The longest of the fixed "active installs" spans, read whatever the window.</summary>
    private const int ActiveSpanDays = 30;

    public async Task<DesktopUsageReadModel> GetAsync(int? windowDays, CancellationToken ct)
    {
        var days = Math.Clamp(windowDays is > 0 ? windowDays.Value : DefaultWindowDays, 1, MaxWindowDays);
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;
        var since = today.AddDays(-(Math.Max(days, ActiveSpanDays) - 1));

        var usage = await store.GetUsageAsync(since, ct);
        var downloads = await store.GetDownloadsAsync(since, ct);

        return Build(
            usage,
            downloads,
            DateTime.SpecifyKind(today, DateTimeKind.Utc),
            days,
            (int)Math.Round(mongoOptions.Value.DesktopUsageRetention.TotalDays));
    }

    internal static DesktopUsageReadModel Build(
        IReadOnlyList<DesktopUsageDay> usage,
        IReadOnlyList<DesktopDownloadDay> downloads,
        DateTime today,
        int windowDays,
        int retentionDays)
    {
        var windowStart = today.AddDays(-(windowDays - 1));
        var inWindow = usage.Where(day => day.DayUtc >= windowStart && day.DayUtc <= today).ToList();
        var downloadsInWindow = downloads.Where(day => day.DayUtc >= windowStart && day.DayUtc <= today).ToList();

        var earliest = inWindow.Select(day => day.DayUtc)
            .Concat(downloadsInWindow.Select(day => day.DayUtc))
            .DefaultIfEmpty()
            .Min();

        return new DesktopUsageReadModel
        {
            WindowDays = windowDays,
            UsageRetentionDays = retentionDays,
            EarliestDayUtc = earliest == default ? null : earliest,
            Active = new DesktopActiveInstalls
            {
                Today = DistinctInstallsSince(usage, today, today),
                Last7Days = DistinctInstallsSince(usage, today.AddDays(-6), today),
                Last30Days = DistinctInstallsSince(usage, today.AddDays(-29), today)
            },
            Totals = new DesktopUsageTotals
            {
                Installs = inWindow.Select(day => day.InstallId).Distinct(StringComparer.Ordinal).Count(),
                NewInstalls = inWindow.Where(day => day.FirstLaunch)
                    .Select(day => day.InstallId).Distinct(StringComparer.Ordinal).Count(),
                Launches = inWindow.Sum(day => (long)day.Launches),
                OpenMinutes = inWindow.Sum(day => (long)day.OpenMinutes),
                ActiveInstallDays = inWindow.Count,
                DownloadsMac = Downloads(downloadsInWindow, "mac"),
                DownloadsWindows = Downloads(downloadsInWindow, "windows")
            },
            Days = earliest == default ? [] : DayBuckets(inWindow, downloadsInWindow, earliest, today),
            Versions = LatestShares(inWindow, day => day.AppVersion),
            OperatingSystems = LatestShares(inWindow, day => day.Os),
            DownloadVersions = downloadsInWindow
                .GroupBy(day => day.Version, StringComparer.Ordinal)
                .OrderByDescending(group => group.Min(day => day.DayUtc))
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DesktopDownloadVersion
                {
                    Version = group.Key,
                    Mac = Downloads(group, "mac"),
                    Windows = Downloads(group, "windows")
                })
                .ToList(),
            Pages = KeyUsage(inWindow, DesktopTelemetryCatalog.Pages, day => day.PageViews),
            Features = KeyUsage(inWindow, DesktopTelemetryCatalog.Features, day => day.Features)
        };
    }

    private static int DistinctInstallsSince(IEnumerable<DesktopUsageDay> usage, DateTime from, DateTime to) =>
        usage.Where(day => day.DayUtc >= from && day.DayUtc <= to)
            .Select(day => day.InstallId)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static long Downloads(IEnumerable<DesktopDownloadDay> downloads, string platform) =>
        downloads.Where(day => day.Platform == platform).Sum(day => day.Count);

    /// <summary>
    /// Zero-filled from the first measured day: inside the measured span a day with no report is
    /// a day nobody opened the app, which is the signal; before it there is nothing to say.
    /// </summary>
    private static List<DesktopUsageDayBucket> DayBuckets(
        List<DesktopUsageDay> usage,
        List<DesktopDownloadDay> downloads,
        DateTime earliest,
        DateTime today)
    {
        var usageByDay = usage.ToLookup(day => day.DayUtc);
        var downloadsByDay = downloads.ToLookup(day => day.DayUtc);
        var buckets = new List<DesktopUsageDayBucket>();
        for (var day = earliest; day <= today; day = day.AddDays(1))
        {
            var installs = usageByDay[day].ToList();
            buckets.Add(new DesktopUsageDayBucket
            {
                DayUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc),
                ActiveInstalls = installs.Count,
                NewInstalls = installs.Count(install => install.FirstLaunch),
                Launches = installs.Sum(install => (long)install.Launches),
                OpenMinutes = installs.Sum(install => (long)install.OpenMinutes),
                DownloadsMac = Downloads(downloadsByDay[day], "mac"),
                DownloadsWindows = Downloads(downloadsByDay[day], "windows")
            });
        }

        return buckets;
    }

    /// <summary>Each install counted once, under the value of its most recent day in the window.</summary>
    private static List<DesktopShare> LatestShares(List<DesktopUsageDay> usage, Func<DesktopUsageDay, string> value) =>
        usage.GroupBy(day => day.InstallId, StringComparer.Ordinal)
            .Select(install => value(install.MaxBy(day => day.DayUtc)!))
            .GroupBy(key => key, StringComparer.Ordinal)
            .Select(group => new DesktopShare { Key = group.Key, Installs = group.Count() })
            .OrderByDescending(share => share.Installs)
            .ThenBy(share => share.Key, StringComparer.Ordinal)
            .ToList();

    private static List<DesktopKeyUsage> KeyUsage(
        List<DesktopUsageDay> usage,
        IReadOnlyList<string> catalog,
        Func<DesktopUsageDay, IReadOnlyDictionary<string, int>> counters) =>
        catalog
            .Select(key => new DesktopKeyUsage
            {
                Key = key,
                Count = usage.Sum(day => (long)counters(day).GetValueOrDefault(key)),
                Installs = usage.Where(day => counters(day).GetValueOrDefault(key) > 0)
                    .Select(day => day.InstallId)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
            })
            .OrderByDescending(entry => entry.Count)
            .ToList();
}
