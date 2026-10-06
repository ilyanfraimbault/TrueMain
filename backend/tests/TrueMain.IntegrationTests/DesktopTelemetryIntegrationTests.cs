using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Desktop;
using Data.Logging.Mongo;
using TrueMain.ReadModels.Ops;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The desktop telemetry end to end (#1805) against real Postgres and Mongo: the app's batches
/// adding up into one document per install and day, the site's downloads counted only with the
/// ingest key, and the admin read requiring the ops key.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DesktopTelemetryIntegrationTests(PostgresFixture postgres, MongoFixture mongo) : IAsyncLifetime
{
    private const string IngestKey = "test-log-ingest-key-0123456789-abcdefghijkl";
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private static readonly Guid InstallId = Guid.Parse("3f2b8c1e-5a4d-4e8f-9b7a-1c2d3e4f5a6b");

    public async ValueTask InitializeAsync() => await mongo.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Batches_AddUpIntoTheInstallsDay()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync("/desktop/telemetry", Batch(firstLaunch: true, draftViews: 2));
        using var second = await client.PostAsJsonAsync("/desktop/telemetry", Batch(firstLaunch: false, draftViews: 3));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var days = await ReadUsageAsync();
        var day = days.Should().ContainSingle("two batches of one day are one document").Subject;
        day.InstallId.Should().Be(InstallId.ToString("D"));
        day.FirstLaunch.Should().BeTrue("a later batch without the flag must not clear it");
        day.Launches.Should().Be(2);
        day.OpenMinutes.Should().Be(10);
        day.PageViews.Should().Equal(new Dictionary<string, int> { ["draft"] = 5 });
    }

    [Fact]
    public async Task Batch_WithAMalformedVersion_IsRejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/desktop/telemetry",
            new { installId = InstallId, appVersion = "latest", os = "macos", launches = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadUsageAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Download_IsCountedWithTheIngestKeyOnly()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var body = new { platform = "windows", version = "0.3.1" };

        using var anonymous = await client.PostAsJsonAsync("/internal/desktop/downloads", body);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/desktop/downloads")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("X-Log-Ingest-Key", IngestKey);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        var usage = await client.GetFromJsonAsync<DesktopUsageReadModel>("/ops/desktop/usage?windowDays=7");
        usage!.Totals.DownloadsWindows.Should().Be(2);
        usage.DownloadVersions.Should().ContainSingle().Which.Version.Should().Be("0.3.1");
    }

    [Fact]
    public async Task Usage_RequiresTheOpsKey()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ops/desktop/usage");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private TrueMainWebApplicationFactory<Program> CreateFactory()
        => new(
            postgres,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:DesktopUsageCollection", MongoFixture.DesktopUsageCollection),
                new KeyValuePair<string, string?>(
                    "MongoLogging:DesktopDownloadsCollection", MongoFixture.DesktopDownloadsCollection),
                new KeyValuePair<string, string?>("LogIngest:ApiKey", IngestKey)
            ]);

    private static object Batch(bool firstLaunch, int draftViews) => new
    {
        installId = InstallId,
        appVersion = "0.3.2-beta.4",
        os = "macos",
        firstLaunch,
        launches = 1,
        openMinutes = 5,
        pageViews = new Dictionary<string, int> { ["draft"] = draftViews, ["unknown"] = 7 }
    };

    private async Task<IReadOnlyList<DesktopUsageDay>> ReadUsageAsync()
    {
        using var context = new MongoLogContext(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            DesktopUsageCollection = MongoFixture.DesktopUsageCollection,
            Enabled = true
        }));
        var store = new DesktopTelemetryStore(context, Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            DesktopUsageCollection = MongoFixture.DesktopUsageCollection,
            DesktopDownloadsCollection = MongoFixture.DesktopDownloadsCollection
        }));
        return await store.GetUsageAsync(DateTime.UtcNow.Date.AddDays(-1), CancellationToken.None);
    }
}
