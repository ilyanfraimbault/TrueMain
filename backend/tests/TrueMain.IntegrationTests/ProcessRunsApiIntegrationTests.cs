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

        // No `since`: the runs list has NO default time lower bound, so every
        // seeded run is returned — including the 10-day-old and 30-day-old rows
        // that predate the rollup's 7-day failure window.
        payload!.Runs.Should().HaveCount(6);
        // Newest started run first.
        payload.Runs[0].ProcessName.Should().Be("MatchIngestion");
        payload.Runs[0].Status.Should().Be("Failed");
        payload.Runs.Should().BeInDescendingOrder(run => run.StartedAtUtc);

        // The status of the latest MatchIngestion run is Failed, but it has an
        // earlier success inside the failure window; one failure counted in the
        // window. The 30-day-old failure is outside the 7-day window, so it is
        // present in the runs list above but NOT counted here.
        var ingestion = payload.Rollup.Single(row => row.ProcessName == "MatchIngestion");
        ingestion.LastStatus.Should().Be("Failed");
        ingestion.FailureCountInWindow.Should().Be(1);
        ingestion.LastSuccessAtUtc.Should().NotBeNull();

        var mainAnalysis = payload.Rollup.Single(row => row.ProcessName == "MainAnalysis");
        mainAnalysis.LastStatus.Should().Be("Success");
        mainAnalysis.FailureCountInWindow.Should().Be(0);

        // Discovery's only run is 10 days old: it predates the failure window but
        // still anchors the rollup's unbounded last-run/last-success.
        var discovery = payload.Rollup.Single(row => row.ProcessName == "Discovery");
        discovery.LastStatus.Should().Be("Success");
        discovery.FailureCountInWindow.Should().Be(0);
        discovery.LastSuccessAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProcessRunsAsync_WithoutSince_ShouldReturnOldRunsCappedByLimitNewestFirst()
    {
        await _fixture.ResetDatabaseAsync();

        // Seed ONLY runs older than the 7-day failure window. The old behaviour
        // (a default now-7d lower bound on the runs list) would return an empty
        // list here — the bug. With the fix the runs list is unbounded by default.
        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            db.ProcessRuns.AddRange(
                BuildRun("LegacyJob", ProcessRunStatus.Success, now.AddDays(-8), error: null, summary: null),
                BuildRun("LegacyJob", ProcessRunStatus.Failed, now.AddDays(-9), error: "old failure", summary: null),
                BuildRun("LegacyJob", ProcessRunStatus.Success, now.AddDays(-30), error: null, summary: null));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // No `since`, limit=2: the two NEWEST runs come back even though all rows
        // are older than 7 days.
        var response = await client.GetAsync("/ops/process-runs?limit=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ProcessRunsTestContract>();
        payload.Should().NotBeNull();

        payload!.Runs.Should().HaveCount(2);
        payload.Runs.Should().BeInDescendingOrder(run => run.StartedAtUtc);
        // The -8d and -9d runs are the two newest; the -30d run is dropped by the limit.
        payload.Runs[0].StartedAtUtc.Should().BeCloseTo(now.AddDays(-8), TimeSpan.FromSeconds(5));
        payload.Runs[1].StartedAtUtc.Should().BeCloseTo(now.AddDays(-9), TimeSpan.FromSeconds(5));

        // The failure window stays bounded at 7 days, independent of the runs list:
        // the -9d failure is outside it, so no failures are counted.
        var legacy = payload.Rollup.Single(row => row.ProcessName == "LegacyJob");
        legacy.FailureCountInWindow.Should().Be(0);
        legacy.LastStatus.Should().Be("Success");
        legacy.LastSuccessAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldSurfaceSummaryJsonAndApplyFilters()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedProcessRunsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Filter to MatchIngestion failures only. Two failures exist (a recent one
        // and a 30-day-old one); with no `since` both come back, newest first.
        var response = await client.GetAsync("/ops/process-runs?processName=MatchIngestion&status=Failed&limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var runs = document.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(2);

        // Newest first: the recent failure carrying the JSON summary leads.
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
            // MatchIngestion: a 30-day-old failure — present in the (now unbounded)
            // runs list but OUTSIDE the 7-day failure window, so it must NOT be
            // counted in failureCountInWindow.
            BuildRun("MatchIngestion", ProcessRunStatus.Failed, now.AddDays(-30), error: "stale failure", summary: null),
            // MainAnalysis: single in-window success.
            BuildRun("MainAnalysis", ProcessRunStatus.Success, now.AddHours(-2), error: null, summary: null),
            // Discovery: a 10-day-old success — outside the 7-day failure window,
            // but with no default lower bound it now appears in the runs list and
            // still anchors the rollup's last-run/last-success for Discovery.
            BuildRun("Discovery", ProcessRunStatus.Success, now.AddDays(-10), error: null, summary: null),
            // Scoring: an in-window success.
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
