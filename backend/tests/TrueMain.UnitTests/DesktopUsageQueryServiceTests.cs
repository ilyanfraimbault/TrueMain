using AwesomeAssertions;
using Data.Desktop;
using TrueMain.Services.Ops.Desktop;

namespace TrueMain.UnitTests;

/// <summary>
/// What the admin page's desktop view (#1805) makes of the per-install days: distinct installs
/// rather than summed documents, each install counted once under its latest version, and a
/// series that starts where the measurement does.
/// </summary>
public class DesktopUsageQueryServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ActiveInstalls_CountEachInstallOncePerSpan()
    {
        var usage = new[]
        {
            Day("a", 0),
            Day("a", 1),
            Day("b", 3),
            Day("c", 20),
            Day("d", 40)
        };

        var model = DesktopUsageQueryService.Build(usage, [], Today, 30, 395);

        model.Active.Today.Should().Be(1);
        model.Active.Last7Days.Should().Be(2, "a reported twice and still is one install");
        model.Active.Last30Days.Should().Be(3, "d reported before the 30-day span");
    }

    [Fact]
    public void Totals_StayInsideTheWindow()
    {
        var usage = new[]
        {
            Day("a", 0, launches: 2, openMinutes: 30, firstLaunch: true),
            Day("a", 1, launches: 1, openMinutes: 10),
            Day("b", 10, launches: 5, openMinutes: 100, firstLaunch: true)
        };

        var model = DesktopUsageQueryService.Build(usage, [], Today, 7, 395);

        model.Totals.Installs.Should().Be(1);
        model.Totals.NewInstalls.Should().Be(1);
        model.Totals.Launches.Should().Be(3);
        model.Totals.OpenMinutes.Should().Be(40);
        model.Totals.ActiveInstallDays.Should().Be(2);
        model.Active.Last30Days.Should().Be(2, "the fixed spans do not follow the window");
    }

    [Fact]
    public void Days_StartAtTheFirstMeasuredDayAndFillTheGapsWithZeros()
    {
        var usage = new[] { Day("a", 3), Day("a", 0) };
        var downloads = new[] { Download(2, "windows", "0.3.1", 4) };

        var model = DesktopUsageQueryService.Build(usage, downloads, Today, 30, 395);

        model.EarliestDayUtc.Should().Be(Today.AddDays(-3));
        model.Days.Select(day => day.ActiveInstalls).Should().Equal(1, 0, 0, 1);
        model.Days.Select(day => day.DownloadsWindows).Should().Equal(0, 4, 0, 0);
        model.Totals.DownloadsWindows.Should().Be(4);
    }

    [Fact]
    public void Days_AreEmptyWhenNothingWasRecorded()
    {
        var model = DesktopUsageQueryService.Build([], [], Today, 30, 395);

        model.Days.Should().BeEmpty("an unmeasured window is not a window of zeros");
        model.EarliestDayUtc.Should().BeNull();
    }

    [Fact]
    public void Versions_CountEachInstallUnderItsLatestDay()
    {
        var usage = new[]
        {
            Day("a", 2, version: "0.3.0"),
            Day("a", 0, version: "0.3.1"),
            Day("b", 1, version: "0.3.1", os: "windows"),
            Day("c", 1, version: "0.3.0")
        };

        var model = DesktopUsageQueryService.Build(usage, [], Today, 30, 395);

        model.Versions.Should().Equal(
            new ReadModels.Ops.DesktopShare { Key = "0.3.1", Installs = 2 },
            new ReadModels.Ops.DesktopShare { Key = "0.3.0", Installs = 1 });
        model.OperatingSystems.Should().Equal(
            new ReadModels.Ops.DesktopShare { Key = "macos", Installs = 2 },
            new ReadModels.Ops.DesktopShare { Key = "windows", Installs = 1 });
    }

    [Fact]
    public void Pages_ListTheWholeCatalogWithViewsAndInstalls()
    {
        var usage = new[]
        {
            Day("a", 0, pages: new Dictionary<string, int> { ["draft"] = 3, ["dashboard"] = 1 }),
            Day("b", 0, pages: new Dictionary<string, int> { ["draft"] = 1 })
        };

        var model = DesktopUsageQueryService.Build(usage, [], Today, 30, 395);

        var draft = model.Pages[0];
        draft.Key.Should().Be("draft");
        draft.Count.Should().Be(4);
        draft.Installs.Should().Be(2);
        model.Pages.Should().Contain(page => page.Key == "recordings" && page.Count == 0,
            "an unused page is a finding, not a missing row");
    }

    [Fact]
    public void DownloadVersions_SplitByPlatformNewestFirst()
    {
        var downloads = new[]
        {
            Download(5, "mac", "0.3.0", 2),
            Download(1, "mac", "0.3.1", 1),
            Download(0, "windows", "0.3.1", 3)
        };

        var model = DesktopUsageQueryService.Build([], downloads, Today, 30, 395);

        model.DownloadVersions.Select(version => version.Version).Should().Equal("0.3.1", "0.3.0");
        model.DownloadVersions[0].Mac.Should().Be(1);
        model.DownloadVersions[0].Windows.Should().Be(3);
        model.Totals.DownloadsMac.Should().Be(3);
    }

    private static DesktopUsageDay Day(
        string install,
        int daysAgo,
        string version = "0.3.1",
        string os = "macos",
        int launches = 1,
        int openMinutes = 5,
        bool firstLaunch = false,
        Dictionary<string, int>? pages = null) =>
        new(
            Today.AddDays(-daysAgo),
            install,
            version,
            os,
            firstLaunch,
            launches,
            openMinutes,
            pages ?? [],
            new Dictionary<string, int>());

    private static DesktopDownloadDay Download(int daysAgo, string platform, string version, long count) =>
        new(Today.AddDays(-daysAgo), platform, version, count);
}
