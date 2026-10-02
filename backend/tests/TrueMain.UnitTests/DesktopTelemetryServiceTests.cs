using AwesomeAssertions;
using Data.Desktop;
using Microsoft.Extensions.Time.Testing;
using TrueMain.Requests.Desktop;
using TrueMain.Requests.Internal;
using TrueMain.Services.Desktop;

namespace TrueMain.UnitTests;

/// <summary>
/// What a batch from the desktop app (#1805) may put in the usage collection, and what it
/// cannot: anything outside the catalog, an unbounded counter, a malformed version.
/// </summary>
public class DesktopTelemetryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordUsage_KeepsOnlyCatalogKeysAndCapsTheirCounts()
    {
        var (service, store) = Create();

        var errors = await service.RecordUsageAsync(
            Request() with
            {
                PageViews = new Dictionary<string, int> { ["draft"] = 2, ["admin"] = 9, ["game"] = 0 },
                Features = new Dictionary<string, int> { ["champSelect"] = 100_000, ["pageViews.x"] = 1 }
            },
            CancellationToken.None);

        errors.Should().BeEmpty();
        var batch = store.Usage.Should().ContainSingle().Subject;
        batch.PageViews.Should().Equal(new Dictionary<string, int> { ["draft"] = 2 });
        batch.Features.Should().Equal(
            new Dictionary<string, int> { ["champSelect"] = DesktopTelemetryService.MaxCountPerKey });
        store.ReceivedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Theory]
    [InlineData("0.3.1")]
    [InlineData("0.3.2-beta.4")]
    public async Task RecordUsage_AcceptsReleaseVersions(string version)
    {
        var (service, store) = Create();

        var errors = await service.RecordUsageAsync(Request() with { AppVersion = version }, CancellationToken.None);

        errors.Should().BeEmpty();
        store.Usage.Should().ContainSingle();
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("0.3")]
    [InlineData("0.3.1 <script>")]
    public async Task RecordUsage_RejectsAnythingElseAsAVersion(string version)
    {
        var (service, store) = Create();

        var errors = await service.RecordUsageAsync(Request() with { AppVersion = version }, CancellationToken.None);

        errors.Should().ContainKey("appVersion");
        store.Usage.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordUsage_RejectsAnEmptyInstallId()
    {
        var (service, store) = Create();

        var errors = await service.RecordUsageAsync(Request() with { InstallId = Guid.Empty }, CancellationToken.None);

        errors.Should().ContainKey("installId");
        store.Usage.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordDownload_CountsAReleaseVersion()
    {
        var (service, store) = Create();

        var errors = await service.RecordDownloadAsync(
            new DesktopDownloadRequest { Platform = "windows", Version = "0.3.1" }, CancellationToken.None);

        errors.Should().BeEmpty();
        store.Downloads.Should().Equal(("windows", "0.3.1"));
    }

    private static (DesktopTelemetryService Service, RecordingStore Store) Create()
    {
        var store = new RecordingStore();
        return (new DesktopTelemetryService(store, new FakeTimeProvider(Now)), store);
    }

    private static DesktopTelemetryRequest Request() => new()
    {
        InstallId = Guid.Parse("3f2b8c1e-5a4d-4e8f-9b7a-1c2d3e4f5a6b"),
        AppVersion = "0.3.1",
        Os = "macos",
        Launches = 1,
        OpenMinutes = 5
    };

    private sealed class RecordingStore : IDesktopTelemetryStore
    {
        public List<DesktopUsageBatch> Usage { get; } = [];

        public List<(string Platform, string Version)> Downloads { get; } = [];

        public DateTime? ReceivedAtUtc { get; private set; }

        public Task<bool> RecordUsageAsync(DesktopUsageBatch batch, DateTime receivedAtUtc, CancellationToken ct)
        {
            Usage.Add(batch);
            ReceivedAtUtc = receivedAtUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordDownloadAsync(string platform, string version, DateTime atUtc, CancellationToken ct)
        {
            Downloads.Add((platform, version));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<DesktopUsageDay>> GetUsageAsync(DateTime sinceDayUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesktopUsageDay>>([]);

        public Task<IReadOnlyList<DesktopDownloadDay>> GetDownloadsAsync(DateTime sinceDayUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesktopDownloadDay>>([]);
    }
}
