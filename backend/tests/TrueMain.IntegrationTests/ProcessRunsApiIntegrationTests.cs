using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public ProcessRunsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldReturnRunsNewestFirstWithRollup()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedProcessRunsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/process-runs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["runs", "rollup"]);

        var firstRun = document.RootElement.GetProperty("runs")[0];
        firstRun.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "id", "processName", "startedAtUtc", "finishedAtUtc",
                "durationMs", "status", "error", "host", "summary"
            ]);

        var firstRollup = document.RootElement.GetProperty("rollup")[0];
        firstRollup.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "processName", "lastStatus", "lastRunAtUtc",
                "lastSuccessAtUtc", "failureCountInWindow"
            ]);

        var payload = await response.Content.ReadFromJsonAsync<ProcessRunsTestContract>();
        payload.Should().NotBeNull();

        // Default window is the last 7 days: the 10-day-old run is excluded.
        payload!.Runs.Should().HaveCount(4);
        // Newest started run first.
        payload.Runs[0].ProcessName.Should().Be("MatchIngestion");
        payload.Runs[0].Status.Should().Be("Failed");
        payload.Runs.Should().BeInDescendingOrder(run => run.StartedAtUtc);

        // The status of the latest MatchIngestion run is Failed, but it has an
        // earlier success inside the window; one failure counted in the window.
        var ingestion = payload.Rollup.Single(row => row.ProcessName == "MatchIngestion");
        ingestion.LastStatus.Should().Be("Failed");
        ingestion.FailureCountInWindow.Should().Be(1);
        ingestion.LastSuccessAtUtc.Should().NotBeNull();

        var mainAnalysis = payload.Rollup.Single(row => row.ProcessName == "MainAnalysis");
        mainAnalysis.LastStatus.Should().Be("Success");
        mainAnalysis.FailureCountInWindow.Should().Be(0);
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldSurfaceSummaryJsonAndApplyFilters()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedProcessRunsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Filter to MatchIngestion failures only.
        var response = await client.GetAsync("/ops/process-runs?processName=MatchIngestion&status=Failed&limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var runs = document.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(1);

        var failedRun = runs[0];
        failedRun.GetProperty("processName").GetString().Should().Be("MatchIngestion");
        failedRun.GetProperty("status").GetString().Should().Be("Failed");
        failedRun.GetProperty("error").GetString().Should().Be("boom: ingest failed");

        // Summary is surfaced verbatim as a JSON object, not a string.
        var summary = failedRun.GetProperty("summary");
        summary.ValueKind.Should().Be(JsonValueKind.Object);
        summary.GetProperty("processed").GetInt32().Should().Be(42);
        summary.GetProperty("stage").GetString().Should().Be("fetch");

        // Rollup is scoped to the same process filter.
        var rollup = document.RootElement.GetProperty("rollup");
        rollup.GetArrayLength().Should().Be(1);
        rollup[0].GetProperty("processName").GetString().Should().Be("MatchIngestion");
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/process-runs");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private async Task SeedProcessRunsAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.ProcessRuns.AddRange(
            // MatchIngestion: an in-window success then a later in-window failure
            // carrying a JSON summary. The failure is the latest run.
            BuildRun("MatchIngestion", ProcessRunStatus.Success, now.AddHours(-5), error: null, summary: null),
            BuildRun(
                "MatchIngestion",
                ProcessRunStatus.Failed,
                now.AddHours(-1),
                error: "boom: ingest failed",
                summary: "{\"processed\":42,\"stage\":\"fetch\"}"),
            // MainAnalysis: single in-window success.
            BuildRun("MainAnalysis", ProcessRunStatus.Success, now.AddHours(-2), error: null, summary: null),
            // Discovery: a 10-day-old success — outside the default 7-day window,
            // so it must be excluded from runs and from failure counting, but it
            // still anchors the rollup's last-run/last-success for Discovery.
            BuildRun("Discovery", ProcessRunStatus.Success, now.AddDays(-10), error: null, summary: null),
            // Scoring: an in-window success so the default query has 4 in-window runs.
            BuildRun("Scoring", ProcessRunStatus.Success, now.AddHours(-3), error: null, summary: null));

        await db.SaveChangesAsync();
    }

    private static ProcessRun BuildRun(
        string processName,
        ProcessRunStatus status,
        DateTime startedAtUtc,
        string? error,
        string? summary)
        => new()
        {
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(2),
            DurationMs = (int)TimeSpan.FromMinutes(2).TotalMilliseconds,
            Status = status,
            Error = error,
            Host = "test-host",
            Summary = summary is null ? null : JsonDocument.Parse(summary)
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class ProcessRunsTestContract
    {
        public IReadOnlyList<ProcessRunTestContract> Runs { get; init; } = [];

        public IReadOnlyList<ProcessRunRollupTestContract> Rollup { get; init; } = [];
    }

    private sealed class ProcessRunTestContract
    {
        public Guid Id { get; init; }

        public string ProcessName { get; init; } = string.Empty;

        public DateTime StartedAtUtc { get; init; }

        public string Status { get; init; } = string.Empty;

        public string? Error { get; init; }
    }

    private sealed class ProcessRunRollupTestContract
    {
        public string ProcessName { get; init; } = string.Empty;

        public string LastStatus { get; init; } = string.Empty;

        public DateTime LastRunAtUtc { get; init; }

        public DateTime? LastSuccessAtUtc { get; init; }

        public int FailureCountInWindow { get; init; }
    }
}
