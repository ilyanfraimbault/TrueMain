using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ExternalProcessRunsApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public ExternalProcessRunsApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ExternalRun_ShouldStartHeartbeatAndFinish_AndReadLikeAnyRun()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var start = await client.PostAsJsonAsync("/ops/process-runs", new
        {
            processName = "personascraper",
            host = "ops-box",
            jobMode = "manual",
            summary = new { currentStep = "leaguepedia" },
        });
        start.StatusCode.Should().Be(HttpStatusCode.Created);
        var started = await start.Content.ReadFromJsonAsync<StartedContract>();
        started!.ProcessName.Should().Be("PersonaScraper");

        var beat = await client.PostAsJsonAsync($"/ops/process-runs/{started.Id}/heartbeat", new
        {
            summary = new { currentStep = "deeplol", progress = new { done = 10, total = 100 } },
        });
        beat.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var running = await GetRunAsync(client, started.Id);
        running.GetProperty("status").GetString().Should().Be("Running");
        running.GetProperty("host").GetString().Should().Be("ops-box");
        running.GetProperty("jobMode").GetString().Should().Be("manual");
        running.GetProperty("summary").GetProperty("currentStep").GetString().Should().Be("deeplol");

        var finish = await client.PostAsJsonAsync($"/ops/process-runs/{started.Id}/finish", new
        {
            status = "success",
            summary = new { personas = 42 },
        });
        finish.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var done = await GetRunAsync(client, started.Id);
        done.GetProperty("status").GetString().Should().Be("Success");
        done.GetProperty("summary").GetProperty("personas").GetInt32().Should().Be(42);
        done.GetProperty("durationMs").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Finish_ShouldKeepTheLastHeartbeatSummary_WhenNoneIsSent()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);
        var id = await StartAsync(client, "OtpSeeder");
        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/heartbeat", new { summary = new { seeded = 7 } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/finish", new { status = "Failed", error = "boom" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var run = await GetRunAsync(client, id);
        run.GetProperty("status").GetString().Should().Be("Failed");
        run.GetProperty("error").GetString().Should().Be("boom");
        run.GetProperty("summary").GetProperty("seeded").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task FinishedRun_ShouldRefuseHeartbeatsAndASecondFinish()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);
        var id = await StartAsync(client, "OtpSeeder");
        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/finish", new { status = "Cancelled" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/heartbeat", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/finish", new { status = "Success" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetRunAsync(client, id)).GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Start_ShouldRejectProcessesOutsideTheAllowList()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        (await client.PostAsJsonAsync("/ops/process-runs", new { processName = "MatchIngestion" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/ops/process-runs", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RunsCollection().CountDocumentsAsync(FilterDefinition<ProcessRunDocument>.Empty))
            .Should().Be(0);
    }

    [Fact]
    public async Task Writes_ShouldNeverTouchAnIngestorRun()
    {
        var ingestorRun = new ProcessRunDocument
        {
            Id = Guid.NewGuid(),
            ProcessName = "MatchIngestion",
            StartedAtUtc = DateTime.UtcNow,
            FinishedAtUtc = DateTime.UtcNow,
            Status = ProcessRunStatus.Running,
            LastHeartbeatAtUtc = DateTime.UtcNow,
        };
        await RunsCollection().InsertOneAsync(ingestorRun);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        (await client.PostAsJsonAsync($"/ops/process-runs/{ingestorRun.Id}/heartbeat", new { summary = new { x = 1 } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/ops/process-runs/{ingestorRun.Id}/finish", new { status = "Failed" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/ops/process-runs/{Guid.NewGuid()}/heartbeat", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var stored = await RunsCollection().Find(doc => doc.Id == ingestorRun.Id).SingleAsync();
        stored.Status.Should().Be(ProcessRunStatus.Running);
        stored.SummaryJson.Should().BeNull();
    }

    [Fact]
    public async Task Writes_ShouldValidateStatusAndSummary()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);
        var id = await StartAsync(client, "PersonaScraper");

        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/finish", new { status = "Running" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/heartbeat", new { summary = new[] { 1, 2 } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var huge = new { log = new string('x', 70 * 1024) };
        (await client.PostAsJsonAsync($"/ops/process-runs/{id}/heartbeat", new { summary = huge }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetRunAsync(client, id)).GetProperty("status").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task Writes_ShouldRequireOpsApiKey()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        (await client.PostAsJsonAsync("/ops/process-runs", new { processName = "OtpSeeder" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"/ops/process-runs/{Guid.NewGuid()}/finish", new { status = "Success" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> StartAsync(HttpClient client, string processName)
    {
        var response = await client.PostAsJsonAsync("/ops/process-runs", new { processName });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StartedContract>())!.Id;
    }

    private static async Task<JsonElement> GetRunAsync(HttpClient client, Guid id)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/ops/process-runs"));
        return document.RootElement.GetProperty("runs").EnumerateArray()
            .Single(run => run.GetProperty("id").GetGuid() == id)
            .Clone();
    }

    private IMongoCollection<ProcessRunDocument> RunsCollection()
        => _mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);

    private sealed class StartedContract
    {
        public Guid Id { get; init; }

        public string ProcessName { get; init; } = string.Empty;
    }
}
